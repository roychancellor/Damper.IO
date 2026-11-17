using Damper.Infrastructure.ReferenceData;

namespace Damper.Setup
{
    public sealed class SetupOptions
    {
        public PostgreSqlConnectionSettings PostgreSql { get; init; } = new();
    }
}