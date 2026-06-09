namespace Damper.Core.Models
{
    public class WebhookEnvelope
    {
        public string CustomerId { get; set; } = "";
        public string DestinationUrl { get; set; } = "";
        public string RawBody { get; set; } = "";
        
        // The Durable Feedback Mechanism:
        public ulong DeliveryTag { get; set; }
        
        // A thread-safe hook to signal that final HTTP processing is complete
        public Func<Task>? OnProcessingCompleteAsync { get; set; }

        public static WebhookEnvelope BuildToPublish(string customerId, string destinationURL, string rawBody)
        {
            return new WebhookEnvelope
            {
                CustomerId = customerId,
                DestinationUrl = destinationURL,
                RawBody = rawBody,
            };
        }
    }
}