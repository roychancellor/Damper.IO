using System.Collections.Concurrent;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Options;
using Damper.Domain.Integrations;

namespace Damper.Infrastructure.DeliveryChannels
{
    public class DeliveryChannelRegistry : IChannelRegistry
    {
        private static readonly ILogger _log = Loggers.Request;
        private static readonly ILogger _traceLog = Loggers.RequestTrace;

        private readonly ConcurrentDictionary<long, EgressPipeline> _registry = new();
        
        // Tracks suspended integration IDs with O(1) thread-safe lookups
        private readonly ConcurrentDictionary<long, byte> _suspendedIntegrations = new();

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

        public async Task<EgressPipeline> GetOrCreatePipelineAsync(long integrationId)
        {
            // FAST FAIL IF INTEGRATION IS SUSPENDED
            if (_suspendedIntegrations.ContainsKey(integrationId))
            {
                _log.Warn($"While attempting to get or create integration pipeline - integration is suspended | INTEG ID: {integrationId}");
                return _suspendedPipeline;
            }

            // REGISTRY HIT WITH CHECK FOR DEAD PIPELINE
            if (_registry.TryGetValue(integrationId, out var pipeline))
            {
                if (!pipeline.BackgroundTask.IsCompleted)
                {
                    _traceLog.Trace($"Channel registry HIT for integration ID {integrationId} - returning writer immediately");
                    return pipeline; // Pipeline is healthy and running
                }
                // If the task is finished (Faulted, Canceled, or RanToCompletion), 
                // treat it as a REGISTRY MISS by evicting it immediately because the pipeline is dead.
                _log.Warn("Stale/Dead pipeline detected for INTEGRATION ID {Id}. Evicting.", integrationId);
                _registry.TryRemove(integrationId, out _);
            }

            // REGISTRY MISS: Lock exclusively to build the pipeline instance safely
            await _initLock.WaitAsync(_ct);
            Integration? currentIntegration;
            try
            {
                _log.Warn($"Channel registry MISS - attempting to lock and build the pipeline safely | INTEG ID: {integrationId}");
                // Double-check lock mitigation pattern
                if (_registry.TryGetValue(integrationId, out pipeline))
                {
                    if (!pipeline.BackgroundTask.IsCompleted)
                    {
                        _log.Debug($"Secondary channel registry HIT for integration ID {integrationId} - returning writer immediately");
                        return pipeline; // Pipeline is healthy and running
                    }
                    _log.Warn("Secondary stale/dead pipeline detected for INTEGRATION ID {Id}. Evicting.", integrationId);
                    _registry.TryRemove(integrationId, out _);
                }

                // If the integration was marked suspended while we were waiting for the lock, catch it here
                if (_suspendedIntegrations.ContainsKey(integrationId))
                {
                    _log.Warn($"While attempting secondary attempt to get or create integration pipeline - integration is suspended | INTEG ID: {integrationId}");
                    return _suspendedPipeline;
                }

                // CREATE A NEW PIPELINE
                currentIntegration = await GetIntegrationConfigAsync(integrationId);
                
                _traceLog.Trace($"Creating integration pipeline from the factory | INTEG ID: {integrationId}");
                void onSuspensionTriggered(long integrationId) => MarkAsSuspended(integrationId);
                pipeline = _pipelineFactory.CreatePipeline(currentIntegration, onSuspensionTriggered, _ct);
                if (!_registry.TryAdd(integrationId, pipeline))
                {
                    _log.Warn($"While attempting to add pipeline to registry, integration Id already existed | INTEG ID: {integrationId}");
                }

                return pipeline;
            }
            finally
            {
                _traceLog.Trace($"Releasing pipeline creation lock | INTEG ID: {integrationId}");
                _initLock.Release();
            }
        }

        private async Task<Integration> GetIntegrationConfigAsync(long integrationId)
        {
            using var scope = _scopeFactory.CreateScope();
            _traceLog.Trace($"Primary + secondary channel registry MISS - getting integration repository and retrieving integration config | INTEG ID: {integrationId}");
            var repo = scope.ServiceProvider.GetRequiredService<IIntegrationRepository>();
            var currentConfig = await repo.GetByIdAsync(integrationId, _ct);
            if (currentConfig == null)
            {
                var msg = $"Configuration missing for integration with ID: {integrationId}";
                _log.Error($"While attempting to get integration config from repository - {msg} | INTEG ID: {integrationId}");
                throw new InvalidOperationException(msg);
            }
            return currentConfig;
        }

        /// <summary>
        /// Invoked by the ChannelDispatcher when an egress endpoint repeatedly fails downstream.
        /// Tears down the operational infrastructure and shifts the registry into a safe non-blocking rejection state.
        /// </summary>
        public void MarkAsSuspended(long integrationId)
        {
            if (!_suspendedIntegrations.TryAdd(integrationId, default))
            {
                _log.Warn("Integration is already suspended | INTEG ID: {id}", integrationId);
                return;
            }
            _log.Error("Circuit Breaker Tripped in Registry for Integration - Tearing down channel | INTEG ID: {id}", integrationId);

            // Evict the pipeline structure completely out of operational memory
            _traceLog.Trace($"Evicting the integration pipeline from the registry | INTEG ID: {integrationId}");
            if (_registry.TryRemove(integrationId, out var pipeline))
            {
                try
                {
                    // Forces the ChannelDispatcher loop to break execution processing naturally
                    _traceLog.Trace($"Completing the existing pipeline channel writer | INTEG ID: {integrationId}");
                    pipeline.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error completing channel writer during pipeline suspension | INTEG ID: {Id}", integrationId);
                }
            }
            // SECONDARY CHECK: If a thread was blocked on _initLock, it might have just finished 
            // adding a "stale" pipeline to the registry. Remove it again.
            if (_registry.TryRemove(integrationId, out var stalePipeline))
            {
                try
                {
                    _traceLog.Trace($"Stale pipeline previously added to registry - removed it here | INTEG ID: {integrationId}");
                    // Forces the ChannelDispatcher loop to break execution processing naturally
                    _traceLog.Trace($"Completing the existing STALE pipeline channel writer | INTEG ID: {integrationId}");
                    stalePipeline.Writer.TryComplete();
                }
                catch (Exception ex)
                {
                    _log.Error(ex, "Error completing stale pipeline channel writer during pipeline suspension | INTEG ID: {Id}", integrationId);
                }
            }

            // Kick off the asynchronous self-healing cooldown task
            // We discard the Task return object ('_ =') because this is designed as fire-and-forget.
            _traceLog.Trace($"Starting cooldown period before attempting to resume the integration | INTEG ID: {integrationId}");
            _ = AutoResumeAfterCooldownAsync(integrationId, TimeSpan.FromSeconds(_optMon.CurrentValue.EgressSettings.CircuitBreakerCooldownSeconds));
        }

        /// <summary>
        /// Non-blocking, stateless timer that handles auto-recovery.
        /// </summary>
        public async Task AutoResumeAfterCooldownAsync(long integrationId, TimeSpan cooldown)
        {
            try
            {
                // Delay using the host application lifetime token so we don't block shutdowns
                await Task.Delay(cooldown, _ct);

                if (_suspendedIntegrations.ContainsKey(integrationId))
                {
                    _log.Warn("Circuit breaker cooldown elapsed for Integration - Attempting automatic self-healing. | INTEG ID: {Id}", integrationId);
                    ResumeIntegration(integrationId);
                }
            }
            catch (OperationCanceledException)
            {
                // Host application is shutting down; ignore and let the task exit cleanly
                _log.Warn($"Application is shutting down during integration cooldown - ignoring | INTEG ID: {integrationId}");
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Uncaught error during the circuit breaker recovery delay | INTEG ID: {Id}.", integrationId);
            }
        }

        /// <summary>
        /// Invoked by your administration tier or dashboard event receiver when an integration endpoint becomes re-enable.
        /// </summary
        public void ResumeIntegration(long integrationId)
        {
            if (_suspendedIntegrations.TryRemove(integrationId, out _))
            {
                // CRITICAL: Ensure no remnants of the "Completed" channel remain 
                // in the dictionary before allowing new ingestion.
                if (_registry.TryRemove(integrationId, out var _))
                {
                    _log.Info("Integration resume: Purging stale, completed pipeline | INTEG ID: {Id}", integrationId);
                }
                
                _log.Info("Circuit Breaker Reset. Resuming ingestion paths for Integration | INTEG ID: {Id}.", integrationId);
            }
        }
    
        public bool IsSuspended(long integrationId)
        {
            return _suspendedIntegrations.ContainsKey(integrationId);
        }

        public void ResetPipeline(long integrationId)
        {
            // Attempt to remove the existing pipeline from the registry
            if (_registry.TryRemove(integrationId, out var oldPipeline))
            {
                _log.Info("Resetting pipeline for integration - Disposing resources | INTEG ID: {Id}", integrationId);

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