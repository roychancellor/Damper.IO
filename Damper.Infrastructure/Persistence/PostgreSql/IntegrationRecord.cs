namespace Damper.Infrastructure.Persistence.PostgreSql;

internal sealed class IntegrationRecord
{
    public long Id { get; init; }

    public string Name { get; init; } = string.Empty;

    public bool Enabled { get; init; }

    public byte[] ApiKeyHash { get; init; } = [];

    public string Configuration { get; init; } = string.Empty;

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset ModifiedAt { get; init; }
}