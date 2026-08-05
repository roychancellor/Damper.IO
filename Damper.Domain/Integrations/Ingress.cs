using Damper.Domain.Common;

namespace Damper.Domain.Integrations
{
    public sealed class Ingress
    {
        public bool Enabled { get; init; } = true;

        public ApiKeyHash ApiKeyHash { get; init; } = new();
    }
}