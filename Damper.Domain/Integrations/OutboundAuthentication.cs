using Damper.Domain.Enums;

namespace Damper.Domain.Integrations
{
    public sealed class OutboundAuthentication
    {
        public AuthenticationType Type { get; init; }

        public string? Username { get; init; }

        public string? Password { get; init; }

        public string? BearerToken { get; init; }

        public string? HeaderName { get; init; }

        public string? HeaderValue { get; init; }
    }
}