namespace Damper.Domain.Integrations
{
    public sealed class IngressEndpoint
    {
        public long Id { get; init; }

        public string Name { get; init; } = string.Empty;

        public bool Enabled { get; init; } = true;

        public long MessageRouteId { get; init; }

        public IngressAuthentication Authentication { get; init; } = new();
    }
}