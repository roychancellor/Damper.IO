namespace Damper.Domain.Integrations
{
    public sealed class IngressEndpoint
{
    public Guid Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; } = true;

    public Guid WebhookRouteId { get; init; }

    public IngressAuthentication Authentication { get; init; } = new();
}
}