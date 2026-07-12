using System.Net.Http.Headers;
using System.Threading.Channels;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Damper.Infrastructure.CustomerChannels
{
    public class ChannelDispatcher
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly string _customerId;
        private readonly ChannelReader<WebhookEnvelope> _reader;
        private readonly IServiceScopeFactory _scopeFactory; // The standard lifecycle bridge
        private static readonly ILogger _log = Loggers.Request;
        private readonly CancellationToken _ct;
        private CustomerConfig _config;

        public ChannelDispatcher(
            IHttpClientFactory httpClientFactory, 
            CustomerConfig initialConfig, 
            ChannelReader<WebhookEnvelope> reader, 
            IServiceScopeFactory scopeFactory,
            CancellationToken ct)
        {
            _httpClientFactory = httpClientFactory;
            _config = initialConfig;
            _customerId = initialConfig.CustomerId;
            _reader = reader;
            _scopeFactory = scopeFactory;
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
                var deliveryTasks = new List<Task>();
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
                    await Task.WhenAll(deliveryTasks);

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
        
        private async Task DeliverWebhookWithRetryAsync(WebhookEnvelope envelope, CustomerConfig config, CancellationToken ct)
        {
            int maxAttempts = 5;
            bool delivered = false;
            TimeSpan retryBackoff = TimeSpan.FromSeconds(2);

            byte[] rawBytes = Convert.FromBase64String(envelope.Base64Payload);

            while (!delivered && envelope.AttemptCount <= maxAttempts)
            {
                var client = _httpClientFactory.CreateClient("DamperEgress");
                
                // Use the verified, fresh destination URL from our config
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
                    // Link your individual 10-second request timeout with the overall application lifecycle token
                    using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                    cts.CancelAfter(TimeSpan.FromSeconds(10));
                    using var response = await client.SendAsync(request, cts.Token);

                    if (response.IsSuccessStatusCode)
                    {
                        delivered = true;
                    }
                    else
                    {
                        envelope.AttemptCount++;
                        await Task.Delay(retryBackoff, ct);
                        retryBackoff *= 2;
                    }
                }
                catch (Exception)
                {
                    envelope.AttemptCount++;
                    await Task.Delay(retryBackoff, ct);
                    retryBackoff *= 2;
                }
            }

            // Trigger the feedback callback loop to execute BasicAck up on the shard worker
            if (envelope.OnProcessingCompleteAsync != null)
            {
                await envelope.OnProcessingCompleteAsync();
            }
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