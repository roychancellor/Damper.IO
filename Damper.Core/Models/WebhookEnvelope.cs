using System.Text.Json;

namespace Damper.Core.Models
{
    public class WebhookEnvelope
    {
        public string CorrelationId { get; set; } = "";
        public string CustomerId { get; set; } = "";
        public string DestinationUrl { get; set; } = "";
        public string RawBody { get; set; } = "";
        
        // The Durable Feedback Mechanism:
        public ulong DeliveryTag { get; set; }
        
        // A thread-safe hook to signal that final HTTP processing is complete
        public Func<Task>? OnProcessingCompleteAsync { get; set; }

        public static WebhookEnvelope BuildToPublish(string correlationId, string customerId, string destinationURL, string rawBody)
        {
            return new WebhookEnvelope
            {
                CorrelationId = correlationId,
                CustomerId = customerId,
                DestinationUrl = destinationURL,
                RawBody = rawBody,
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