using Damper.Domain.Common;
using Damper.Domain.Enums;

namespace Damper.Domain.Integrations.OutAuthentication
{
    public sealed record CustomHeaderAuthentication : OutboundAuthentication
    {
        public override AuthenticationType Type => AuthenticationType.CustomHeader;

        public string HeaderName { get; init; } = string.Empty;

        public Secret HeaderValue { get; init; } = new();
    }
}