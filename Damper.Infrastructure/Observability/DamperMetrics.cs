using System.Diagnostics.Metrics;

namespace Damper.Infrastructure.Observability
{
    public static class DamperMetrics
    {
        private static readonly Meter Meter = new("Damper.Core", "1.0.0");

        public static readonly Counter<long> ParkedForRetryCounter = 
            Meter.CreateCounter<long>("damper.messages.parked", "messages", "Count of messages moved to parking lot");

        public static readonly Counter<long> SentToDeadLetter = 
            Meter.CreateCounter<long>("damper.messages.dead.lettered", "messages", "Count of messages moved to DLQ");

        public static readonly Counter<long> DeliverySuccessCounter = 
            Meter.CreateCounter<long>("damper.messages.delivered", "messages", "Count of successful message deliveries");

        public static readonly Counter<long> UnroutableMessageCounter = 
            Meter.CreateCounter<long>("damper.messages.unroutable", "messages", "Count of unroutable message messages");
    }
}