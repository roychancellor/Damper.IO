using System.Net.Http.Headers;
using System.Text.Json;
using Damper.Core.Models;

namespace Damper.Core.OutboundService
{
    public class DamperOutboundService : IOutboundService
    {
        private HttpClient _httpClient;

        public DamperOutboundService(HttpClient httpClient)
        {
            _httpClient = httpClient;
        }
        
        public async Task DeliverToCustomerAsync(string rabbitMqMessage)
        {
            try
            {
                // 1. Deserialize Damper.IO's internal packaging
                var envelope = JsonSerializer.Deserialize<WebhookEnvelope>(rabbitMqMessage);
                if (envelope == null)
                {
                    return;
                }
    
                // 2. Unbox the exact, original, unmodified bytes
                var originalWebhookBytes = Convert.FromBase64String(envelope.Base64Payload);

                var request = new HttpRequestMessage(HttpMethod.Post, envelope.DestinationUrl)
                {
                    // 3. ASSIGN THE RAW BYTES DIRECTLY TO THE CONTENT BODY
                    // Using ByteArrayContent guarantees .NET will not touch or translate the data.
                    Content = new ByteArrayContent(originalWebhookBytes)
                };

                // 4. Parrot the filtered headers back onto the request
                foreach (var header in envelope.Headers)
                {
                    string key = header.Key;
                    string val = header.Value;
    
                    // Skip the headers that HttpClient handles automatically or that must match the target domain
                    if (key.Equals("Host", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) ||
                        key.Equals("Connection", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }
    
                    // Some headers belong to the content wrapper specifically (like content-type)
                    if (key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
                    {
                        continue; // Handled by StringContent above
                    }
    
                    // Try adding to request headers safely
                    request.Headers.TryAddWithoutValidation(key, val);
                }
                
                // Explicitly enforce the original Content-Type header on the content wrapper
                if (envelope.Headers.TryGetValue("Content-Type", out var contentType))
                {
                    request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(contentType);
                }
    
                // 5. Stamp your Damper.IO metadata
                request.Headers.Add("X-Damper-Received-At", envelope.ReceivedAt.ToString("o"));
                request.Headers.Add("X-Damper-Delivery-Attempt", envelope.AttemptCount.ToString());
    
                // 6. Fire it off to the customer's server
                var response = await _httpClient.SendAsync(request);
            }
            catch (Exception)
            {
                throw;
            }
        }
    }
}