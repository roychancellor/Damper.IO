using System.Net;
using System.Net.Http.Headers;
using System.Threading.Channels;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Observability;
using Damper.Infrastructure.ReferenceData;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;

namespace Damper.Infrastructure.CustomerChannels
{
    public class ChannelDispatcher : IDispatcher
    {
        private static readonly ILogger _log = Loggers.Request;
        private static readonly ILogger _traceLog = Loggers.RequestTrace;

        private static bool SUCCESS = true;
        private static bool FAILURE = false;
        private static bool KEEP_ALIVE = true;
        
        private readonly IOptionsMonitor<AppSettings> _optMon;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Action<string> _onSuspensionTriggered;
        private readonly string _customerId;
        private readonly ChannelReader<WebhookEnvelope> _reader;
        private readonly IServiceScopeFactory _scopeFactory; // The standard lifecycle bridge
        private readonly ObjectPool<WebhookAckContext> _contextPool;
        private readonly CancellationToken _ct;
        private CustomerConfig _custConfig;

        public ChannelDispatcher(
            IOptionsMonitor<AppSettings> optMon,
            IHttpClientFactory httpClientFactory, 
            Action<string> onSuspensionTriggered, 
            CustomerConfig initialConfig, 
            ChannelReader<WebhookEnvelope> reader, 
            IServiceScopeFactory scopeFactory,
            ObjectPool<WebhookAckContext> contextPool,
            CancellationToken ct)
        {
            _optMon = optMon;
            _httpClientFactory = httpClientFactory;
            _onSuspensionTriggered = onSuspensionTriggered;
            _custConfig = initialConfig;
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

            try
            {
                while (await _reader.WaitToReadAsync(ct))
                {
                    _traceLog.Trace($"Entering while loop awaiting messages from the channel sender");
                    var deliveryTasks = new List<Task<bool>>();
                    int messagesInBatch = 0;

                    // Drain up to the maximum burst capacity allowed for this interval window
                    // or until there are no more messages to read
                    while (messagesInBatch.IsBelowCustomerRate(_custConfig) && _reader.TryRead(out var envelope))
                    {
                        deliveryTasks.Add(DeliverWebhookWithRetryAsync(envelope, _custConfig, ct));
                        messagesInBatch++;
                    }

                    if (deliveryTasks.Count == 0) { continue; }

                    _traceLog.Trace($"Messages actively being sent to customer endpoint - awaiting all delivery tasks for the batch");

                    // Execute the outbound HTTP burst concurrently
                    var deliveryResults = await Task.WhenAll(deliveryTasks);

                    _traceLog.Trace($"All delivery tasks completed for the batch");

                    // If any single message in this batch completely failed after exhausting internal retries,
                    // trip the circuit breaker immediately.
                    if (deliveryResults.HasAtLeastOneError())
                    {
                        _traceLog.Trace($"Delivery tasks completed with error(s) | ERROR COUNT: {deliveryResults.Count(r => r == false)}");
                        _log.Error("Circuit breaker triggered for Customer {Id} due to exhausted retry count.", _customerId);

                        // Drain anything still buffered but unread - it was pulled off RabbitMQ and is unacked,
                        // but no delivery task will ever pick it up once this loop exits. Park it for automatic
                        // retry after cooldown instead of leaving it stranded until the app restarts.
                        while (_reader.TryRead(out var leftover))
                        {
                            await leftover.FinalizeParkAsync(_contextPool);
                        }
                        _onSuspensionTriggered(_customerId);
                        return;
                    }

                    // Only enforce the pacing delay if there is still data waiting in the channel.
                    // This prevents adding artificial latency to lone, sporadic trickle messages.
                    if (_reader.HasDataWaiting())
                    {
                        _traceLog.Trace($"There are new messages but waiting for the configured delivery time for a predictable recovery window.");
                        // Guarantees a true, predictable recovery window between outbound bursts
                        await Task.Delay(TimeSpan.FromMilliseconds(_custConfig.DeliveryIntervalMillis), ct);
                    }

                    // Sync configuration definitions once per processing cycle
                    await RefreshConfigAsync(ct);
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

        public async Task RefreshConfigAsync(CancellationToken ct)
        {
            try
            {
                _traceLog.Trace($"Refreshing customer configuration | CUST ID: {_customerId}");

                using var scope = _scopeFactory.CreateScope();
                var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
                var freshConfig = await repo.GetByIdAsync(_customerId, ct);

                if (freshConfig != null)
                {
                    _custConfig = freshConfig;
                }
            }
            catch (Exception ex)
            {
                // Do not crash the entire consumer if the repository is temporarily down
                _log.Warn("Failed to refresh pacing configuration for customer {CustomerId}. Maintaining last known state.", _customerId, ex);
            }
        }
        
        public async Task<bool> DeliverWebhookWithRetryAsync(WebhookEnvelope envelope, CustomerConfig custConfig, CancellationToken ct)
        {
            try
            {
                // Allow NLog to automatically populate the Correlation Id in every log statement in this method
                using var correlationScope = _log.BeginCorrelationScope(envelope.CorrelationId);

                _traceLog.Debug($"DeliverWebhookWithRetryAsync starting | CUST ID: {envelope.CustomerId} | DEST: {envelope.DestinationUrl}");
                
                var maxAttempts = _optMon.CurrentValue.EgressSettings.MaxSendAttempts;
                var retryBackoff = TimeSpan.FromMilliseconds(_optMon.CurrentValue.EgressSettings.RetryBackoffMillis);

                while (envelope.HasAttemptsRemaining(maxAttempts))
                {
                    var client = _httpClientFactory.CreateClient(_optMon.CurrentValue.EgressSettings.HttpClientName);

                    using var httpRequest = envelope.BuildHttpRequest(custConfig);

                    _traceLog.Debug("Getting all HTTP headers ready for request");
                    httpRequest.AddOriginalRequestHeaders(envelope, _optMon.CurrentValue.EgressSettings.SystemHeaders);
                    if (!httpRequest.TryHandleContentTypeHeader(envelope))
                    {
                        _log.Fatal($"Content-Type header is not parsable - rejecting to DLQ | CUST ID: {envelope.CustomerId} | CORR ID: {envelope.CorrelationId}");
                        await envelope.FinalizeRejectAsync(_contextPool);
                        return KEEP_ALIVE; // Keep the pipeline loop alive
                    }
                    httpRequest.AddDamperHeaders(envelope);

                    try
                    {
                        _log.Info($"====> Sending envelope to customer | CUST ID: {envelope.CustomerId} | URL: {envelope.DestinationUrl}");
                        _traceLog.Debug($"Sending HTTP POST request now | CUST ID: {envelope.CustomerId} | URL: {envelope.DestinationUrl}");
                        using var cts = CancellationTokenSource
                                        .CreateLinkedTokenSource(ct)
                                        .SetRequestTimeout(_optMon.CurrentValue.EgressSettings.RequestTimeoutMillis);

                        using var response = await client.SendAsync(httpRequest, cts.Token);

                        _traceLog.Debug($"Response received | CUST ID: {envelope.CustomerId} | HTTP STATUS: {response.StatusCode}");
                        if (response.IsSuccessStatusCode)
                        {
                            _log.Info($"<==== Customer returned SUCCESS | CUST ID: {envelope.CustomerId} | HTTP STATUS: {response.StatusCode}");
                            DamperMetrics.DeliverySuccessCounter.Add(1, new KeyValuePair<string, object?>(DamperConstants.DAMPER_METER_CUSTOMER_ID, envelope.CustomerId));
                            await envelope.FinalizeAckAsync(_contextPool);
                            return SUCCESS;
                        }

                        if (response.StatusCode.Is4XX() && !response.StatusCode.IsTooManyRequests())
                        {
                            _log.Fatal($"<==== Customer returned 4XX status code (not 429) - Sending to dead letter | CUST ID: {envelope.CustomerId} | HTTP STATUS: {response.StatusCode}");
                            await envelope.FinalizeRejectAsync(_contextPool);
                            return KEEP_ALIVE; // Keep the pipeline loop alive
                        }

                        _log.Warn($"<==== Customer request FAILED (try {envelope.AttemptCount}) - retrying with exponential backoff ({retryBackoff.Seconds} sec) | CUST ID: {envelope.CustomerId} | HTTP STATUS: {response.StatusCode}");
                        envelope.AttemptCount++;
                        retryBackoff = await DoExponentialBackoff(retryBackoff, ct);
                    }
                    catch (Exception ex)
                    {
                        _log.Error("<==== Transient error delivering webhook for {Id}. Attempt {Attempt} - Executing retry with exponential backoff ", envelope.CustomerId, envelope.AttemptCount, ex);
                        envelope.AttemptCount++;
                        retryBackoff = await DoExponentialBackoff(retryBackoff, ct);
                    }
                }

                _log.Error("<==== Exhausted retries for customer {Id} - Parking for delayed automatic retry.", envelope.CustomerId);
                await envelope.FinalizeParkAsync(_contextPool); // Send to the parking lot for a time out/retry (the other special paths above will send to DLQ if necessary)
                return FAILURE;
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

        private async Task<TimeSpan> DoExponentialBackoff(TimeSpan retryBackoff, CancellationToken ct)
        {
            var jitterMilliBase = _optMon.CurrentValue.EgressSettings.RetryBackoffJitterMillis;
            int jitterMs = Random.Shared.Next(-jitterMilliBase, jitterMilliBase);
            var totalBackoff = retryBackoff + TimeSpan.FromMilliseconds(jitterMs);

            await Task.Delay(totalBackoff > TimeSpan.Zero ? totalBackoff : retryBackoff, ct);
            return retryBackoff * 2;
        }
    }

    public static class DispatcherExtensions
    {
        public static bool IsBelowCustomerRate(this int messagesInBatch, CustomerConfig _custConfig)
        {
            return messagesInBatch < _custConfig.DeliveryRate;
        }

        public static bool Is4XX(this HttpStatusCode statusCode)
        {
            return (int)statusCode >= 400 && (int)statusCode <= 499;    
        }

        public static bool IsTooManyRequests(this HttpStatusCode statusCode)
        {
            return statusCode == HttpStatusCode.TooManyRequests;
        }

        public static bool IsSystemHeader(this string key, HashSet<string> systemHeaders)
        {
            return systemHeaders.Contains(key);
        }

        public static bool HasAtLeastOneError(this bool[] results)
        {
            return results.Any(success => !success);
        }

        public static bool HasDataWaiting(this ChannelReader<WebhookEnvelope> reader)
        {
            return reader.CanCount && reader.Count > 0;
        }

        public static HttpRequestMessage AddOriginalRequestHeaders(this HttpRequestMessage request, WebhookEnvelope envelope, HashSet<string> systemHeaders)
        {
            foreach (var header in envelope.Headers)
            {
                if (header.Key.IsSystemHeader(systemHeaders)) { continue; }
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            return request;
        }

        public static bool TryHandleContentTypeHeader(this HttpRequestMessage httpRequest, WebhookEnvelope envelope)
        {
            if (envelope.Headers.TryGetValue("Content-Type", out var contentType))
            {
                // This is validated at ingress, but check again here. If it somehow made it here unparsable, send to DLQ.
                if (!MediaTypeHeaderValue.TryParse(contentType, out MediaTypeHeaderValue? mediaHeader))
                {
                    return false;
                }
                httpRequest.Content?.Headers.ContentType = mediaHeader;
            }
            return true;
        }

        public static void AddDamperHeaders(this HttpRequestMessage httpRequest, WebhookEnvelope envelope)
        {
            httpRequest.Headers.Add(DamperConstants.REQUEST_X_DAMPER_CUSTOMER_ID, envelope.CorrelationId);
            httpRequest.Headers.Add(DamperConstants.REQUEST_X_DAMPER_DELIVERY_ATTEMPT, envelope.AttemptCount.ToString());
        }

        public static CancellationTokenSource SetRequestTimeout(this CancellationTokenSource cts, int timeoutMillis)
        {
            cts.CancelAfter(TimeSpan.FromMilliseconds(timeoutMillis));
            return cts;
        }
    }
}