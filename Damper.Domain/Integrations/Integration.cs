using Damper.Domain.Common;

namespace Damper.Domain.Integrations
{
    /*
    Integration
    |
    |-- "Who am I?"
    |       Name
    |       Description
    |       Enabled
    |
    |-- "How do requests enter?"
    |       Ingress
    |
    |-- "How do requests leave?"
            DElivery
    */
    /*
    FUTURE ADMIN API:
    POST /integrations
    GET /integrations/{id}
    PUT /integrations/{id}
    DELETE /integrations/{id}
    */
    public sealed class Integration
    {
        public long Id { get; init; }

        public IntegrationName Name { get; init; } = new(string.Empty);

        public string? Description { get; init; }

        public bool Enabled { get; init; } = true;

        public Ingress Ingress { get; init; } = new();

        public Delivery Delivery { get; init; } = new();

        public DateTimeOffset CreatedUtc { get; init; }

        public DateTimeOffset ModifiedUtc { get; init; }
    }
}