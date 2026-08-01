namespace Damper.Domain.Integrations
{
    public sealed class DeliverySettings
    {
        public int RequestsPerSecond { get; init; }

        public int MaxRetryAttempts { get; init; }

        public TimeSpan InitialRetryDelay { get; init; }

        public double RetryBackoffMultiplier { get; init; }

        public TimeSpan MaximumRetryDelay { get; init; }

        public TimeSpan RequestTimeout { get; init; }
    }
}