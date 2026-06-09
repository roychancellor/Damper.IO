namespace Damper.Infrastructure.Repositories
{
    public class CustomerConfig
    {
        public string CustomerId { get; set; } = "";
        public string SecretKey { get; set; } = "";
        public string WebhookHeaderKey { get; set; } = "";
        public string DestinationURL { get; set; } = "";
        public int DeliveryRate { get; set; }
        public int DeliveryIntervalMillis { get; set; }
    }
}