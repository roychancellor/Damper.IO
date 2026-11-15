namespace Damper.Domain.Integrations
{
    // Properties have what we consider "sensible" defaults which
    // the Admin UI will be able to override.
    public sealed class DeliverySettings
    {
        public int RequestsPerInterval { get; init; } = 10;

        public int DeliveryIntervalMillis { get; set; } = 1000;

        public int MaxRetryAttempts { get; init; } = 5;

        public int InitialRetryDelayMillis { get; init; } = 1000;

        public double RetryBackoffMultiplier { get; init; } = 2.0;

        public long MaximumRetryDelayMillis { get; init; } = 30000;

        public int RequestTimeoutMillis { get; init; } = 10000;

        public int MaxQueueCapacity { get; set; } = 1000;
    }
}