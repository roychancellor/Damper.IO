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

        public ChannelDispatcher(
            IHttpClientFactory httpClientFactory, 
            string customerId, 
            ChannelReader<WebhookEnvelope> reader, 
            IServiceScopeFactory scopeFactory,
            CancellationToken ct)
        {
            _httpClientFactory = httpClientFactory;
            _customerId = customerId;
            _reader = reader;
            _scopeFactory = scopeFactory;
            _ct = ct;
        }

        public async Task RunLoopAsync(CancellationToken ct)
        {
            while (await _reader.WaitToReadAsync(ct))
            {
                while (_reader.TryRead(out var envelope))
                {
                    CustomerConfig? currentConfig;

                    // Create a brief scope to execute the cached read safely across threads
                    using (var scope = _scopeFactory.CreateScope())
                    {
                        var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
                        currentConfig = await repo.GetByIdAsync(_customerId, ct);
                    }

                    if (currentConfig == null)
                    {
                        _log.LogError("Configuration missing for customer {CustomerId}", _customerId);
                        await Task.Delay(TimeSpan.FromSeconds(5), ct);
                        continue;
                    }

                    var interval = TimeSpan.FromMilliseconds(currentConfig.DeliveryIntervalMillis);
                    using var periodicTimer = new PeriodicTimer(interval);

                    var deliveryTasks = new List<Task>();
                    int messagesInBatch = 0;
                    WebhookEnvelope? currentEnvelope = envelope;

                    // 2. Build out the parallel execution chunk based on the current delivery rate
                    while (currentEnvelope != null && messagesInBatch < currentConfig.DeliveryRate)
                    {
                        deliveryTasks.Add(DeliverWebhookWithRetryAsync(currentEnvelope, currentConfig, ct));
                        messagesInBatch++;

                        if (messagesInBatch < currentConfig.DeliveryRate)
                        {
                            _reader.TryRead(out currentEnvelope);
                        }
                    }

                    // 3. Discharge the batch to the customer destination
                    await Task.WhenAll(deliveryTasks);

                    // 4. Await the next interval cycle tick before pulling any more messages
                    await periodicTimer.WaitForNextTickAsync(ct);
                }
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