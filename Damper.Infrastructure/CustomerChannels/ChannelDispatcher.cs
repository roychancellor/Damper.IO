using System.Net.Http.Headers;
using System.Threading.Channels;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;

namespace Damper.Infrastructure.CustomerChannels
{
    public class ChannelDispatcher
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Action<string> _onSuspensionTriggered;
        private readonly string _customerId;
        private readonly ChannelReader<WebhookEnvelope> _reader;
        private readonly IServiceScopeFactory _scopeFactory; // The standard lifecycle bridge
        private readonly ObjectPool<WebhookAckContext> _contextPool;
        private static readonly ILogger _log = Loggers.Request;
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
            var interval = TimeSpan.FromMilliseconds(_config.DeliveryIntervalMillis);
            using var periodicTimer = new PeriodicTimer(interval);

            while (await _reader.WaitToReadAsync(ct))
            {
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
                    // Execute the outbound HTTP burst concurrently
                    var results = await Task.WhenAll(deliveryTasks);

                    // If any single message in this batch completely failed after exhausting internal retries,
                    // trip the circuit breaker immediately.
                    if (results.Any(success => !success))
                    {
                        _log.LogCritical("Circuit breaker triggered for Customer {Id} due to exhausted retries.", _customerId);
                        _onSuspensionTriggered(_customerId);
                        
                        // Break out of the loop. The registry completion code will tear down this pipeline.
                        return;
                    }

                    // Only enforce the pacing delay if there is still data waiting in the channel.
                    // This prevents adding artificial latency to lone, sporadic trickle messages.
                    if (_reader.CanCount && _reader.Count > 0)
                    {
                        // Guarantees a true, predictable recovery window between outbound bursts
                        await Task.Delay(TimeSpan.FromMilliseconds(_config.DeliveryIntervalMillis), ct);
                        // If the downstream endpoint takes longer to receive the batch than the length
                        // of the periodic timer, then this step gets skipped and it immediately loops
                        // back and can pull/send more messages. This might be shocking to the customer
                        // so it's probably best to avoid this.
                        // TODO: Decide later whether to remove this permanently or let it behave this
                        // way (or make it a feature flag).
                        //await periodicTimer.WaitForNextTickAsync(ct);
                    }

                    // Sync configuration definitions once per processing cycle
                    await RefreshConfigAsync(ct);
                }
            }
        }

        private async Task RefreshConfigAsync(CancellationToken ct)
        {
            try
            {
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
                _log.LogWarning(ex, "Failed to refresh pacing configuration for customer {CustomerId}. Maintaining last known state.", _customerId);
            }
        }
        
        private async Task<bool> DeliverWebhookWithRetryAsync(WebhookEnvelope envelope, CustomerConfig config, CancellationToken ct)
        {
            bool delivered = false;
            try
            {
                // TODO: Get max attempts and retry backoff from app config
                int maxAttempts = 5;
                TimeSpan retryBackoff = TimeSpan.FromSeconds(2);

                byte[] rawBytes = Convert.FromBase64String(envelope.Base64Payload);

                while (!delivered && envelope.AttemptCount <= maxAttempts)
                {
                    var client = _httpClientFactory.CreateClient("DamperEgress");
                    using var request = new HttpRequestMessage(HttpMethod.Post, config.DestinationURL);
                    request.Content = new ByteArrayContent(rawBytes);

                    foreach (var header in envelope.Headers)
                    {
                        if (IsSystemHeader(header.Key)) continue;
                        request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                    }

                    if (envelope.Headers.TryGetValue("Content-Type", out var contentType))
                    {
                        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                    }

                    request.Headers.Add("X-Damper-Correlation-Id", envelope.CorrelationId);
                    request.Headers.Add("X-Damper-Delivery-Attempt", envelope.AttemptCount.ToString());

                    try
                    {
                        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                        cts.CancelAfter(TimeSpan.FromSeconds(10));
                        using var response = await client.SendAsync(request, cts.Token);

                        if (response.IsSuccessStatusCode)
                        {
                            delivered = true; // while loop sentinel
                        }
                        else
                        {
                            envelope.AttemptCount++;
                            retryBackoff = await DoExponentialBackoff(retryBackoff, ct);
                        }
                    }
                    catch (Exception)
                    {
                        envelope.AttemptCount++;
                        retryBackoff = await DoExponentialBackoff(retryBackoff, ct);
                    }
                }

                // Execute the zero-allocation callback to ACK the message
                if (envelope.AckContext != null)
                {
                    await envelope.AckContext.AckAsync();
                }
            }
            finally
            {
                // Crucial step: return object memory to the provider pool even if
                // unexpected runtime exceptions occur above.
                if (envelope.AckContext != null)
                {
                    _contextPool.Return(envelope.AckContext);
                }
            }

            return delivered;
        }

        private static async Task<TimeSpan> DoExponentialBackoff(TimeSpan retryBackoff, CancellationToken ct)
        {
            // ADD JITTER: Smear the retry window by +/- 20% to break concurrency synchronization
            // TODO: Get jitter values from app config
            int jitterMs = Random.Shared.Next(-200, 200);
            var totalBackoff = retryBackoff + TimeSpan.FromMilliseconds(jitterMs);

            await Task.Delay(totalBackoff > TimeSpan.Zero ? totalBackoff : retryBackoff, ct);
            retryBackoff *= 2;
            return retryBackoff;
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