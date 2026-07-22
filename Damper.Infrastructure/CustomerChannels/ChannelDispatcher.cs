using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Observability;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Damper.Infrastructure.CustomerChannels
{
    public class ChannelDispatcher
    {
        private static readonly ILogger _log = Loggers.Request;
        private static readonly ILogger _traceLog = Loggers.RequestTrace;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Action<string> _onSuspensionTriggered;
        private readonly string _customerId;
        private readonly ChannelReader<WebhookEnvelope> _reader;
        private readonly IServiceScopeFactory _scopeFactory; // The standard lifecycle bridge
        private readonly ObjectPool<WebhookAckContext> _contextPool;
        private readonly CancellationToken _ct;
        private CustomerConfig _config;

        public ChannelDispatcher(
            IHttpClientFactory httpClientFactory, 
            Action<string> onSuspensionTriggered, 
            CustomerConfig initialConfig, 
            ChannelReader<WebhookEnvelope> reader, 
            IServiceScopeFactory scopeFactory,
            ObjectPool<WebhookAckContext> contextPool,
            CancellationToken ct)
        {
            _httpClientFactory = httpClientFactory;
            _onSuspensionTriggered = onSuspensionTriggered;
            _config = initialConfig;
            _customerId = initialConfig.CustomerId;
            _reader = reader;
            _scopeFactory = scopeFactory;
            _contextPool = contextPool;
            _ct = ct;
        }

        // Fixed pacing pattern: consume continuously up to the rate limit,
        // and only enforce the timer delay if there are more messages waiting to be processed.
        public async Task RunLoopAsync(CancellationToken ct)
        {
            _traceLog.Trace($"RunLoopAsync starting");

            var interval = TimeSpan.FromMilliseconds(_config.DeliveryIntervalMillis);
            using var periodicTimer = new PeriodicTimer(interval);

            try
            {
                while (await _reader.WaitToReadAsync(ct))
                {
                    _traceLog.Trace($"Entering while loop awaiting messages from the channel sender");
                    var deliveryTasks = new List<Task<bool>>();
                    int messagesInBatch = 0;
    
                    // Drain up to the maximum burst capacity allowed for this interval window
                    // or until there are no more messages to read
                    while (messagesInBatch < _config.DeliveryRate && _reader.TryRead(out var envelope))
                    {
                        deliveryTasks.Add(DeliverWebhookWithRetryAsync(envelope, _config, ct));
                        messagesInBatch++;
                    }
    
                    if (deliveryTasks.Count > 0)
                    {
                        _traceLog.Trace($"Batch of messages ready to send - awaiting all delivery tasks for the batch");

                        // Execute the outbound HTTP burst concurrently
                        var results = await Task.WhenAll(deliveryTasks);
                        
                        _traceLog.Trace($"Delivery tasks completed for the batch");

                        var completedWithErrors = results.Any(success => !success);
                        if (completedWithErrors)
                        {
                            _traceLog.Trace($"Delivery tasks completed with error(s) | ERROR COUNT: {results.Count(r => r == false)}");
                        }
    
                        // If any single message in this batch completely failed after exhausting internal retries,
                        // trip the circuit breaker immediately.
                        if (completedWithErrors)
                        {
                            _log.Error("Circuit breaker triggered for Customer {Id} due to exhausted retry count.", _customerId);
                            _onSuspensionTriggered(_customerId);
                            
                            // Break out of the loop. The registry completion code will tear down this pipeline.
                            return;
                        }
    
                        // Only enforce the pacing delay if there is still data waiting in the channel.
                        // This prevents adding artificial latency to lone, sporadic trickle messages.
                        if (_reader.CanCount && _reader.Count > 0)
                        {
                            _traceLog.Trace($"There are new messages but waiting for the configured delivery time for a predictable recovery window.");
                            // Guarantees a true, predictable recovery window between outbound bursts
                            await Task.Delay(TimeSpan.FromMilliseconds(_config.DeliveryIntervalMillis), ct);
                        }
    
                        // Sync configuration definitions once per processing cycle
                        await RefreshConfigAsync(ct);
                    }
                }
            }
            finally
            {
                // 1. Notify the Registry immediately (synchronously)
                // The loop is done, so the registry MUST remove the reference now.
                _onSuspensionTriggered(_customerId);

                // 2. Check for completion exceptions
                // You can inspect the channel's completion status directly via the Reader
                if (_reader.Completion.IsFaulted)
                {
                    var exception = _reader.Completion.Exception?.Flatten();
                    _log.Error("Channel pipeline faulted for Customer {Id}.", _customerId, exception);
                }
                else
                {
                    _log.Info("Channel pipeline finalized for Customer {Id}.", _customerId);
                }
            }
        }

        private async Task RefreshConfigAsync(CancellationToken ct)
        {
            try
            {
                _traceLog.Trace($"Refreshing customer configuration | CUST ID: {_customerId}");

                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
                var freshConfig = await repo.GetByIdAsync(_customerId, ct);

                if (freshConfig != null)
                {
                    _config = freshConfig;
                }
            }
            catch (Exception ex)
            {
                // Do not crash the entire consumer if the repository is temporarily down
                _log.Warn("Failed to refresh pacing configuration for customer {CustomerId}. Maintaining last known state.", _customerId, ex);
            }
        }
        
        private async Task<bool> DeliverWebhookWithRetryAsync(WebhookEnvelope envelope, CustomerConfig config, CancellationToken ct)
        {
            try
            {
                // Allow NLog to automatically populate the Correlation Id in every log statement in this method
                using var correlationScope = _log.BeginCorrelationScope(envelope.CorrelationId);

                _traceLog.Debug($"DeliverWebhookWithRetryAsync starting | CUST ID: {envelope.CustomerId} | DEST: {envelope.DestinationUrl}");
                
                // TODO: Get these from appsettings
                int maxAttempts = 5;
                TimeSpan retryBackoff = TimeSpan.FromSeconds(2);

                while (envelope.AttemptCount <= maxAttempts)
                {
                    var client = _httpClientFactory.CreateClient("DamperEgress");
                    using var request = new HttpRequestMessage(HttpMethod.Post, config.DestinationURL);
                    request.Content = new ReadOnlyMemoryContent(envelope.RawPayloadBytes);

                    _traceLog.Debug("Getting all HTTP headers ready for request");
                    foreach (var header in envelope.Headers)
                    {
                        if (IsSystemHeader(header.Key)) { continue; }
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    if (envelope.Headers.TryGetValue("Content-Type", out var contentType))
                    {
                        // This is validated at ingress, but check again here. If it somehow made it here unparsable, send to DLQ.
                        var isContentTypeHeaderParsable = MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mediaHeader);
                        if (!isContentTypeHeaderParsable)
                        {
                            _log.Fatal($"Content-Type header is not parsable | CUST ID: {envelope.CustomerId} | CORR ID: {envelope.CorrelationId}");
                            await FinalizeRejectAsync(envelope);
                            return true; // Return true to keep the pipeline loop alive
                        }
                        request.Content.Headers.ContentType = mediaHeader;
                    }
                    request.Headers.Add("X-Damper-Correlation-Id", envelope.CorrelationId);
                    request.Headers.Add("X-Damper-Delivery-Attempt", envelope.AttemptCount.ToString());

                    try
                    {
                        _traceLog.Debug($"Sending request | CUST ID: {envelope.CustomerId} | URL: {envelope.DestinationUrl}");
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(10));
                        using var response = await client.SendAsync(request, cts.Token);

                        _traceLog.Debug($"Response received | CUST ID: {envelope.CustomerId} | HTTP STATUS: {response.StatusCode}");
                        if (response.IsSuccessStatusCode)
                        {
                            _log.Info($"Response IS successful | CUST ID: {envelope.CustomerId} | HTTP STATUS: {response.StatusCode}");
                            DamperMetrics.DeliverySuccessCounter.Add(1, new KeyValuePair<string, object?>("customer_id", envelope.CustomerId));
                            await FinalizeAckAsync(envelope);
                            return true;
                        }
                        
                        if (Is4XX(response.StatusCode) && !IsTooManyRequests(response.StatusCode))
                        {
                            _log.Fatal($"Customer returned 4XX status code - Sending to dead letter | CUST ID: {envelope.CustomerId} | HTTP STATUS: {response.StatusCode}");
                            await FinalizeRejectAsync(envelope);
                            return true; // Return true to keep the pipeline loop alive
                        }
                        
                        _log.Warn($"Response NOT successful (try {envelope.AttemptCount}) - Executing retry with exponential backoff | CUST ID: {envelope.CustomerId} | HTTP STATUS: {response.StatusCode}");
                        envelope.AttemptCount++;
                        retryBackoff = await DoExponentialBackoff(retryBackoff, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("Transient error delivering webhook for {Id}. Attempt {Attempt} - Executing retry with exponential backoff ", envelope.CustomerId, envelope.AttemptCount, ex);
                        envelope.AttemptCount++;
                        retryBackoff = await DoExponentialBackoff(retryBackoff, ct);
                    }
                }

                _log.Error("Exhausted retries for {Id} - Sending to dead letter.", envelope.CustomerId);
                await FinalizeRejectAsync(envelope);
                return false; // This might kill the pipeline loop!!!
            }
            finally
            {
                if (envelope.AckContext != null)
                {
                    _contextPool.Return(envelope.AckContext);
                    envelope.AckContext = null;
                }
                _traceLog.Trace($"DeliverWebhookWithRetryAsync finished | CUST ID: {_customerId}");
            }
        }

        private async Task FinalizeAckAsync(WebhookEnvelope envelope)
        {
            if (envelope.AckContext != null)
            {
                await envelope.AckContext.AckAsync();
                _contextPool.Return(envelope.AckContext);
                envelope.AckContext = null;
            }
        }

        private async Task FinalizeRejectAsync(WebhookEnvelope envelope)
        {
            if (envelope.AckContext != null)
            {
                await envelope.AckContext.RejectAsync(requeue: false);
                _contextPool.Return(envelope.AckContext);
                envelope.AckContext = null;
            }
        }

        private static bool Is4XX(HttpStatusCode statusCode)
        {
            return (int)statusCode >= 400 && (int)statusCode <= 499;    
        }

        private static bool IsTooManyRequests(HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests;
        }

        private static async Task<TimeSpan> DoExponentialBackoff(TimeSpan retryBackoff, CancellationToken ct)
        {
            int jitterMs = Random.Shared.Next(-200, 200);
            var totalBackoff = retryBackoff + TimeSpan.FromMilliseconds(jitterMs);

            await Task.Delay(totalBackoff > TimeSpan.Zero ? totalBackoff : retryBackoff, ct);
            return retryBackoff * 2;
        }

        private static bool IsSystemHeader(string key)
        {
            return key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("Connection", StringComparison.OrdinalIgnoreCase) ||
                   key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase);
        }
    }
}