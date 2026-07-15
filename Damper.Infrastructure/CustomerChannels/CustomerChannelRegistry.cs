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
        private readonly SuspendedChannelWriter _suspendedWriter = new();

        private readonly CancellationToken _ct;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IEgressPipelineFactory _pipelineFactory;
        private readonly SemaphoreSlim _initLock = new(1, 1);

        public CustomerChannelRegistry(
            IEgressPipelineFactory egressPipelineFactory, 
            IServiceScopeFactory scopeFactory, 
            IHostApplicationLifetime appLifetime)
        {
            _pipelineFactory = egressPipelineFactory;
            _scopeFactory = scopeFactory;
            _ct = appLifetime.ApplicationStopping;
        }

        public async Task<ChannelWriter<WebhookEnvelope>> GetOrCreateChannel(string customerId)
        {
            // 1. FAST CIRCUIT PATH: Check suspension state instantly without touching any pipelines or dictionary lookups
            if (_suspendedCustomers.ContainsKey(customerId))
            {
                return _suspendedWriter;
            }

            // 2. HAPPY PATH: Registry cache hit (No locks, highly concurrent)
            if (_registry.TryGetValue(customerId, out var pipeline))
            {
                _log.Debug($"Channel registry HIT for customer ID {customerId} - returning writer immediately");
                return pipeline.Writer;
            }

            // 3. REGISTRY MISS: Lock exclusively to build the pipeline instance safely
            await _initLock.WaitAsync(_ct);
            CustomerConfig? currentConfig;
            try
            {
                // Double-check lock mitigation pattern
                if (_registry.TryGetValue(customerId, out pipeline))
                {
                    return pipeline.Writer;
                }

                // If the customer was marked suspended while we were waiting for the lock, catch it here
                if (_suspendedCustomers.ContainsKey(customerId))
                {
                    return _suspendedWriter;
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
                
                return pipeline.Writer;
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
            }
        }

        /// <summary>
        /// Invoked by your administration tier or dashboard event receiver when a customer re-enables their endpoint.
        /// </summary>
        public void ResumeCustomer(string customerId)
        {
            if (_suspendedCustomers.TryRemove(customerId, out _))
            {
                _log.LogInformation("Circuit Breaker Reset. Resuming ingestion paths for Customer {Id}.", customerId);
            }
        }
    }
}