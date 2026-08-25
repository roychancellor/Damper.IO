using Damper.Domain.Common;
using Damper.Domain.Enums;

namespace Damper.Domain.Integrations.OutAuthentication
{
    public sealed record BearerAuthentication : OutboundAuthentication
    {
        public override AuthenticationType Type => AuthenticationType.Bearer;

        public Secret Token { get; init; } = new();
    }
}