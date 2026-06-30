using System.Text.Json;

namespace Damper.Infrastructure.Models
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

        public static WebhookEnvelope BuildBase(RequestWrapper rw)
        {
            var toReturn = new WebhookEnvelope
            {
              CorrelationId = rw.CorrelationId,
              CustomerId = rw.CustomerId,
              ReceivedAt = DateTime.UtcNow,
              AttemptCount = 1,  
            };
            return toReturn;
        }

        public WebhookEnvelope SetDestination(string toSet)
        {
            DestinationUrl = toSet;
            return this;
        }

        public WebhookEnvelope SetPayload(string toSet)
        {
            Base64Payload = toSet;
            return this;
        }

        public WebhookEnvelope SetHeaders(Dictionary<string, string> toSet)
        {
            Headers = toSet;
            return this;
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