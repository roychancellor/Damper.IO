using System.Collections.Concurrent;
using Damper.Infrastructure.ChannelRegistry;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Options;
using Damper.Infrastructure.CustomerChannels;

namespace Damper.Infrastructure.DeliveryChannels
{
    public class DeliveryChannelRegistry : IChannelRegistry
    {
        private static readonly ILogger _log = Loggers.Request;
        private static readonly ILogger _traceLog = Loggers.RequestTrace;

        private readonly ConcurrentDictionary<string, EgressPipeline> _registry = new();
        
        // Tracks suspended customer IDs with O(1) thread-safe lookups
        private readonly ConcurrentDictionary<string, byte> _suspendedCustomers = new();

        private readonly CancellationToken _ct;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEgressPipelineFactory _pipelineFactory;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private readonly IOptionsMonitor<AppSettings> _optMon;

        private static readonly EgressPipeline _suspendedPipeline = new(
            new SuspendedChannelWriter(), 
            Task.CompletedTask, // Represents a "finished" healthy task
            CancellationTokenSource.CreateLinkedTokenSource(CancellationToken.None)
        );

        public DeliveryChannelRegistry(IEgressPipelineFactory egressPipelineFactory, 
                                       IServiceScopeFactory scopeFactory, 
                                       IHostApplicationLifetime appLifetime,
                                       IOptionsMonitor<AppSettings> optMon)
        {
            _pipelineFactory = egressPipelineFactory;
            _scopeFactory = scopeFactory;
            _ct = appLifetime.ApplicationStopping;
            _optMon = optMon;
        }

        public async Task<EgressPipeline> GetOrCreatePipelineAsync(string customerId)
        {
            // FAST FAIL IF CUSTOMER IS SUSPENDED
            if (_suspendedCustomers.ContainsKey(customerId))
            {
                _log.Warn($"While attempting to get or create customer pipeline - customer is suspended | CUST ID: {customerId}");
                return _suspendedPipeline;
            }

            // REGISTRY HIT WITH CHECK FOR DEAD PIPELINE
            if (_registry.TryGetValue(customerId, out var pipeline))
            {
                if (!pipeline.BackgroundTask.IsCompleted)
                {
                    _traceLog.Trace($"Channel registry HIT for customer ID {customerId} - returning writer immediately");
                    return pipeline; // Pipeline is healthy and running
                }
                // If the task is finished (Faulted, Canceled, or RanToCompletion), 
                // treat it as a REGISTRY MISS by evicting it immediately because the pipeline is dead.
                _log.Warn("Stale/Dead pipeline detected for CUSTOMER ID {Id}. Evicting.", customerId);
                _registry.TryRemove(customerId, out _);
            }

            // REGISTRY MISS: Lock exclusively to build the pipeline instance safely
            await _initLock.WaitAsync(_ct);
            CustomerConfig? currentConfig;
            try
            {
                _log.Warn($"Channel registry MISS - attempting to lock and build the pipeline safely | CUST ID: {customerId}");
                // Double-check lock mitigation pattern
                if (_registry.TryGetValue(customerId, out pipeline))
                {
                    if (!pipeline.BackgroundTask.IsCompleted)
                    {
                        _log.Debug($"Secondary channel registry HIT for customer ID {customerId} - returning writer immediately");
                        return pipeline; // Pipeline is healthy and running
                    }
                    _log.Warn("Secondary stale/dead pipeline detected for CUSTOMER ID {Id}. Evicting.", customerId);
                    _registry.TryRemove(customerId, out _);
                }

                // If the customer was marked suspended while we were waiting for the lock, catch it here
                if (_suspendedCustomers.ContainsKey(customerId))
                {
                    _log.Warn($"While attempting secondary attempt to get or create customer pipeline - customer is suspended | CUST ID: {customerId}");
                    return _suspendedPipeline;
                }

                // CREATE A NEW PIPELINE
                currentConfig = await GetCustomerConfigAsync(customerId);
                
                _traceLog.Trace($"Creating customer pipeline from the factory | CUST ID: {customerId}");
                void onSuspensionTriggered(string customerId) => MarkAsSuspended(customerId);
                pipeline = _pipelineFactory.CreatePipeline(currentConfig, onSuspensionTriggered, _ct);
                if (!_registry.TryAdd(customerId, pipeline))
                {
                    _log.Warn($"While attempting to add pipeline to registry, customer Id already existed | CUST ID: {customerId}");
                }

                return pipeline;
            }
            finally
            {
                _traceLog.Trace($"Releasing pipeline creation lock | CUST ID: {customerId}");
                _initLock.Release();
            }
        }

        private async Task<CustomerConfig> GetCustomerConfigAsync(string customerId)
        {
            using var scope = _scopeFactory.CreateScope();
            _traceLog.Trace($"Primary + secondary channel registry MISS - getting customer repository and retrieving customer config | CUST ID: {customerId}");
            var repo = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
            var currentConfig = await repo.GetByIdAsync(customerId, _ct);
            if (currentConfig == null)
            {
                var msg = $"Configuration missing for customer: {customerId}";
                _log.Error($"While attempting to get customer config from repository - {msg} | CUST ID: {customerId}");
                throw new InvalidOperationException(msg);
            }
            return currentConfig;
        }

        /// <summary>
        /// Invoked by the ChannelDispatcher when an egress endpoint repeatedly fails downstream.
        /// Tears down the operational infrastructure and shifts the registry into a safe non-blocking rejection state.
        /// </summary>
        public void MarkAsSuspended(string customerId)
        {
            if (!_suspendedCustomers.TryAdd(customerId, default))
            {
                _log.Warn("Customer is already suspended | CUST ID: {id}", customerId);
                return;
            }
            _log.Error("Circuit Breaker Tripped in Registry for Customer - Tearing down channel | CUST ID: {id}", customerId);

            // Evict the pipeline structure completely out of operational memory
            _traceLog.Trace($"Evicting the customer pipeline from the registry | CUST ID: {customerId}");
            if (_registry.TryRemove(customerId, out var pipeline))
            {
                try
                {
                    // Forces the ChannelDispatcher loop to break execution processing naturally
                    _traceLog.Trace($"Completing the existing pipeline channel writer | CUST ID: {customerId}");
                    pipeline.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error completing channel writer during pipeline suspension | CUST ID: {Id}", customerId);
                }
            }
            // SECONDARY CHECK: If a thread was blocked on _initLock, it might have just finished 
            // adding a "stale" pipeline to the registry. Remove it again.
            if (_registry.TryRemove(customerId, out var stalePipeline))
            {
                try
                {
                    _traceLog.Trace($"Stale pipeline previously added to registry - removed it here | CUST ID: {customerId}");
                    // Forces the ChannelDispatcher loop to break execution processing naturally
                    _traceLog.Trace($"Completing the existing STALE pipeline channel writer | CUST ID: {customerId}");
                    stalePipeline.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error completing stale pipeline channel writer during pipeline suspension | CUST ID: {Id}", customerId);
                }
            }

            // Kick off the asynchronous self-healing cooldown task
            // We discard the Task return object ('_ =') because this is designed as fire-and-forget.
            _traceLog.Trace($"Starting cooldown period before attempting to resume the customer | CUST ID: {customerId}");
            _ = AutoResumeAfterCooldownAsync(customerId, TimeSpan.FromSeconds(_optMon.CurrentValue.EgressSettings.CircuitBreakerCooldownSeconds));
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
                    _log.Warn("Circuit breaker cooldown elapsed for Customer - Attempting automatic self-healing. | CUST ID: {Id}", customerId);
                    ResumeCustomer(customerId);
                }
            }
            catch (OperationCanceledException)
            {
                // Host application is shutting down; ignore and let the task exit cleanly
                _log.Warn($"Application is shutting down during customer cooldown - ignoring | CUST ID: {customerId}");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Uncaught error during the circuit breaker recovery delay | CUST ID: {Id}.", customerId);
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
                if (_registry.TryRemove(customerId, out var _))
                {
                    _log.Info("Customer resume: Purging stale, completed pipeline | CUST ID: {Id}", customerId);
                }
                
                _log.Info("Circuit Breaker Reset. Resuming ingestion paths for Customer | CUST ID: {Id}.", customerId);
            }
        }
    
        public bool IsSuspended(string customerId)
        {
            return _suspendedCustomers.ContainsKey(customerId);
        }

        public void ResetPipeline(string customerId)
        {
            // Attempt to remove the existing pipeline from the registry
            if (_registry.TryRemove(customerId, out var oldPipeline))
            {
                _log.Info("Resetting pipeline for customer - Disposing resources | CUST ID: {Id}", customerId);

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