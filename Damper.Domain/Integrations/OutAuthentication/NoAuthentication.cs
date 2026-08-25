using Damper.Domain.Enums;

namespace Damper.Domain.Integrations.OutAuthentication
{
    public sealed record NoAuthentication : OutboundAuthentication
    {
        public override AuthenticationType Type => AuthenticationType.None;
    }
}