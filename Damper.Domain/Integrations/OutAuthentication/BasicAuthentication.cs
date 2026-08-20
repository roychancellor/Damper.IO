using Damper.Domain.Common;
using Damper.Domain.Enums;

namespace Damper.Domain.Integrations.OutAuthentication
{
    public sealed record BasicAuthentication : OutboundAuthentication
    {
        public override AuthenticationType Type => AuthenticationType.Basic;

        public string Username { get; init; } = string.Empty;

        public Secret Password { get; init; } = new();
    }
}