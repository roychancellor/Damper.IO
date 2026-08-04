namespace Damper.Domain.Integrations
{
    public sealed class DeliverySettings
    {
        public int RequestsPerInterval { get; init; }
        
        public int DeliveryIntervalMillis { get; set; }

        public int MaxRetryAttempts { get; init; }

        public int InitialRetryDelayMillis { get; init; }

        public double RetryBackoffMultiplier { get; init; }

        public long MaximumRetryDelayMillis { get; init; }

        public int RequestTimeoutMillis { get; init; }
        
        public int MaxQueueCapacity { get; set; }
    }
}