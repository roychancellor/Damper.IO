using System.Text.Json;

namespace Damper.Core.Models
{
    public class WebhookEnvelope
    {
        public string CorrelationId { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public string DestinationUrl { get; set; } = "";
        public string Base64Payload { get; set; } = "";
        public Dictionary<string, string> Headers { get; set; } = [];
        public DateTime ReceivedAt { get; set; }
        public int AttemptCount { get; set; }
        
        // The Durable Feedback Mechanism:
        public ulong DeliveryTag { get; set; }
        
        // A thread-safe hook to signal that final HTTP processing is complete
        public Func<Task>? OnProcessingCompleteAsync { get; set; }

        public static WebhookEnvelope BuildToPublish(string correlationId, string customerId, string destinationURL, string base64Payload, Dictionary<string, string> headers)
        {
            return new WebhookEnvelope
            {
                CorrelationId = correlationId,
                CustomerId = customerId,
                DestinationUrl = destinationURL,
                Base64Payload = base64Payload,
                Headers = headers,
                ReceivedAt = DateTime.UtcNow,
                AttemptCount = 1,
            };
        }

        public string Jsonify()
        {
            try
            {
                var toReturn = JsonSerializer.Serialize(this);
                return toReturn;
            }
            catch (Exception)
            {
                return string.Empty;
            }
        }
    }
}