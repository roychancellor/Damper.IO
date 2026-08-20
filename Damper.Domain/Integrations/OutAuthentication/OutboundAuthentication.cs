using Damper.Domain.Enums;

namespace Damper.Domain.Integrations.OutAuthentication
{
    public abstract record OutboundAuthentication
    {
        public abstract AuthenticationType Type { get; }
    }
}