namespace Damper.Domain.Integrations
{
    public sealed class Delivery
    {
        public bool Enabled { get; init; } = true;

        public Destination Destination { get; init; } = new();

        public DeliverySettings Settings { get; init; } = new();

        public OutboundAuthentication Authentication { get; init; } = new();

        public HeaderCollection Headers { get; init; } = new();
    }
}