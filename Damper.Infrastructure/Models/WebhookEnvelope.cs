namespace Damper.Infrastructure.Models
{
    public class WebhookEnvelope
    {
        public string CorrelationId { get; set; } = string.Empty;
        public string CustomerId { get; set; } = string.Empty;
        public string DestinationUrl { get; set; } = string.Empty;
        public ReadOnlyMemory<byte> RawPayloadBytes { get; set; }
        public Dictionary<string, string> Headers { get; set; } = [];
        public DateTime ReceivedAt { get; set; }
        public int AttemptCount { get; set; } = 1;
       
        // Uee a Webhook Ack Context that will come from an object pool to
        // eliminate the use of an anonymous "OnProcessingCompleteAsync" lambda
        public WebhookAckContext? AckContext { get; set; }

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

        public WebhookEnvelope SetPayload(ReadOnlyMemory<byte> toSet)
        {
            RawPayloadBytes = toSet;
            return this;
        }

        public WebhookEnvelope SetHeaders(Dictionary<string, string> toSet)
        {
            Headers = toSet;
            return this;
        }
    }
}