using Damper.Domain.Common;
using Damper.Domain.Enums;

namespace Damper.Domain.Integrations
{
    public sealed class OutboundAuthentication
    {
        public AuthenticationType Type { get; init; }

        public Secret? Username { get; init; }

        public Secret? Password { get; init; }

        public Secret? BearerToken { get; init; }

        public Secret? HeaderName { get; init; }

        public Secret? HeaderValue { get; init; }
    }
}