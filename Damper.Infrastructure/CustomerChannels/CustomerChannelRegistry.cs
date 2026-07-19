using System.Collections.Concurrent;
using System.Threading.Channels;
using Damper.Infrastructure.ChannelRegistry;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;

namespace Damper.Infrastructure.CustomerChannels
{
    public class CustomerChannelRegistry : IChannelRegistry
    {
        private static readonly ILogger _log = Loggers.Request;

        private readonly ConcurrentDictionary<string, CustomerEgressPipeline> _registry = new();
        
        // Tracks suspended customer IDs with O(1) thread-safe lookups
        private readonly ConcurrentDictionary<string, byte> _suspendedCustomers = new();

        private readonly CancellationToken _ct;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEgressPipelineFactory _pipelineFactory;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        private static readonly CustomerEgressPipeline _suspendedPipeline = new(
            new SuspendedChannelWriter(), 
            Task.CompletedTask, // Represents a "finished" healthy task
            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None)
        );

        public CustomerChannelRegistry(
            IEgressPipelineFactory egressPipelineFactory, 
            IServiceScopeFactory scopeFactory, 
            IHostApplicationLifetime appLifetime)
        {
            _pipelineFactory = egressPipelineFactory;
            _scopeFactory = scopeFactory;
            _ct = appLifetime.ApplicationStopping;
        }

        public async Task<CustomerEgressPipeline> GetOrCreatePipelineAsync(string customerId)
        {
            // 1. FAST CIRCUIT PATH: Check suspension state instantly without touching any pipelines or dictionary lookups
            if (_suspendedCustomers.ContainsKey(customerId))
            {
                return _suspendedPipeline;
            }

            // 2. HAPPY PATH: Registry cache hit (No locks, highly concurrent)
            if (_registry.TryGetValue(customerId, out var pipeline))
            {
                // If the task is finished (Faulted, Canceled, or RanToCompletion), 
                // the pipeline is dead. Evict it immediately.
                if (pipeline.BackgroundTask.IsCompleted)
                {
                    _log.Warn("Stale/Dead pipeline detected for CUSTOMER ID {Id}. Evicting.", customerId);
                    _registry.TryRemove(customerId, out _);
                }
                else
                {
                    _log.Debug($"Channel registry HIT for customer ID {customerId} - returning writer immediately");
                    return pipeline; // Pipeline is healthy and running
                }
            }

            // 3. REGISTRY MISS: Lock exclusively to build the pipeline instance safely
            await _initLock.WaitAsync(_ct);
            CustomerConfig? currentConfig;
            try
            {
                // Double-check lock mitigation pattern
                if (_registry.TryGetValue(customerId, out pipeline))
                {
                    if (pipeline.BackgroundTask.IsCompleted)
                    {
                        _log.Warn("Stale/Dead pipeline detected for CUSTOMER ID {Id}. Evicting.", customerId);
                        _registry.TryRemove(customerId, out _);
                    }
                    else
                    {
                        _log.Debug($"Channel registry HIT for customer ID {customerId} - returning writer immediately");
                        return pipeline; // Pipeline is healthy and running
                    }

                }

                // If the customer was marked suspended while we were waiting for the lock, catch it here
                if (_suspendedCustomers.ContainsKey(customerId))
                {
                    return _suspendedPipeline;
                }

                using (var scope = _scopeFactory.CreateScope())
                {
                    _log.Debug($"Channel registry MISS for customer ID {customerId} - getting customer repository and retrieving customer config.");
                    var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
                    currentConfig = await repo.GetByIdAsync(customerId, _ct);
                    if (currentConfig == null)
                    {
                        var msg = $"Configuration missing for customer: {customerId}";
                        _log.Error($"While attempting to get customer config from repository - {msg}");
                        throw new InvalidOperationException(msg);
                    }
                }

                pipeline = _pipelineFactory.CreatePipeline(currentConfig, customerId => MarkAsSuspended(customerId), _ct);
                if (!_registry.TryAdd(customerId, pipeline))
                {
                    _log.Warn($"While attempting to add pipeline to registry, customer Id already existed | CUST ID: {customerId}");
                }
                
                return pipeline;
            }
            finally
            {
                _initLock.Release();
            }
        }

        /// <summary>
        /// Invoked by the ChannelDispatcher when an egress endpoint repeatedly fails downstream.
        /// Tears down the operational infrastructure and shifts the registry into a safe non-blocking rejection state.
        /// </summary>
        public void MarkAsSuspended(string customerId)
        {
            if (_suspendedCustomers.TryAdd(customerId, default))
            {
                _log.LogCritical("Circuit Breaker Tripped in Registry for Customer {Id}. Suspending channel.", customerId);

                // Evict the pipeline structure completely out of operational memory
                if (_registry.TryRemove(customerId, out var pipeline))
                {
                    try
                    {
                        // Forces the ChannelDispatcher loop to break execution processing naturally
                        pipeline.Writer.TryComplete();
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error completing channel writer during pipeline suspension for Customer {Id}", customerId);
                    }
                }
                // SECONDARY CHECK: If a thread was blocked on _initLock, it might have just finished 
                // adding a "stale" pipeline to the registry. Remove it again.
                if (_registry.TryRemove(customerId, out var stalePipeline))
                {
                    try
                    {
                        // Forces the ChannelDispatcher loop to break execution processing naturally
                        stalePipeline.Writer.TryComplete();
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Error completing stape pipeline channel writer during pipeline suspension for Customer {Id}", customerId);
                    }
                }

                // 1. Kick off the asynchronous self-healing cooldown task
                // We discard the Task return object ('_ =') because this is designed as fire-and-forget.
                // TODO: Get the cooldown duration from the customer's config (e.g., currentConfig.CircuitBreakerCooldownSeconds).
                _ = AutoResumeAfterCooldownAsync(customerId, TimeSpan.FromSeconds(10)/*TimeSpan.FromMinutes(5)*/);
            }
        }

        /// <summary>
        /// Non-blocking, stateless timer that handles auto-recovery.
        /// </summary>
        public async Task AutoResumeAfterCooldownAsync(string customerId, TimeSpan cooldown)
        {
            try
            {
                // Delay using the host application lifetime token so we don't block shutdowns
                await Task.Delay(cooldown, _ct);

                if (_suspendedCustomers.ContainsKey(customerId))
                {
                    _log.Warn("Circuit breaker cooldown elapsed for Customer {Id}. Attempting automatic self-healing.", customerId);
                    ResumeCustomer(customerId);
                }
            }
            catch (OperationCanceledException)
            {
                // Host application is shutting down; ignore and let the task exit cleanly
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Uncaught error during the circuit breaker recovery delay for Customer {Id}.", customerId);
            }
        }

        /// <summary>
        /// Invoked by your administration tier or dashboard event receiver when a customer re-enables their endpoint.
        /// </summary>
        public void ResumeCustomer(string customerId)
        {
            if (_suspendedCustomers.TryRemove(customerId, out _))
            {
                // CRITICAL: Ensure no remnants of the "Completed" channel remain 
                // in the dictionary before allowing new ingestion.
                if (_registry.TryRemove(customerId, out var oldPipeline))
                {
                    _log.Info("Purging stale, completed pipeline during resume for {Id}", customerId);
                }
                
                _log.Info("Circuit Breaker Reset. Resuming ingestion paths for Customer {Id}.", customerId);
            }
        }
    
        public bool IsSuspended(string customerId)
        {
            return _suspendedCustomers.ContainsKey(customerId);
        }

        public void EvictPipeline(string customerId) => _registry.TryRemove(customerId, out _);

        public void ResetPipeline(string customerId)
        {
            // Attempt to remove the existing pipeline from the registry
            if (_registry.TryRemove(customerId, out var oldPipeline))
            {
                _log.Info("Resetting pipeline for {Id}. Disposing resources.", customerId);

                // Safely complete the writer if it isn't already
                // This signals to any downstream consumers that no more data is coming
                try 
                { 
                    oldPipeline.Writer.TryComplete(); 
                } 
                catch { /* Ignore - it might already be completed */ }

                // If your Pipeline object implements IDisposable (recommended), call it.
                // This ensures CancellationTokenSources or HttpClient instances are cleared.
                if (oldPipeline is IDisposable disposable)
                {
                    disposable.Dispose();
                }
            }
        }
    }
}