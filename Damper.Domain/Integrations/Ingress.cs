namespace Damper.Domain.Integrations
{
    public sealed class Ingress
    {
        public bool Enabled { get; init; } = true;

        public IngressAuthentication Authentication { get; init; } = new();
    }
}