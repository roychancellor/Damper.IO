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
    |       IngressEndpoint
    |
    |-- "How do requests leave?"
            WebhookRoute
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

        public string Name { get; init; } = string.Empty;

        public string? Description { get; init; }

        public bool Enabled { get; init; } = true;

        public long Version { get; init; }

        public IngressEndpoint Ingress { get; init; } = new();

        public MessageRoute Route { get; init; } = new();

        public DateTimeOffset CreatedUtc { get; init; }

        public DateTimeOffset ModifiedUtc { get; init; }
    }
}