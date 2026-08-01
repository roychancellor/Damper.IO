namespace Damper.Domain.Integrations
{
    public sealed class MessageRoute
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public long Version { get; init; }

    public MessageDestination Target { get; init; } = new();

    public DeliverySettings Delivery { get; init; } = new();

    public OutboundAuthentication Authentication { get; init; } = new();

    public HeaderCollection Headers { get; init; } = new();
}
}