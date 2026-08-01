using Damper.Domain.Enums;

namespace Damper.Domain.Integrations
{
    public sealed class IngressAuthentication
    {
        public AuthenticationType Type { get; init; }

        public string Secret { get; init; } = string.Empty;
    }
}