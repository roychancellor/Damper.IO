using Damper.Infrastructure.Extensions;
using Damper.Infrastructure.ReferenceData;
using Npgsql;

namespace Damper.Setup.PostgreSql;

public sealed class PostgreSqlInspector(PostgreSqlConnectionSettings settings)
{
    public async Task InspectAsync(CancellationToken cancellationToken = default)
    {
        Console.WriteLine("PostgreSQL");
        Console.WriteLine("----------");
        Console.WriteLine($"Host:     {settings.HostName}");
        Console.WriteLine($"Port:     {settings.Port}");
        Console.WriteLine($"Database: {settings.Database}");
        Console.WriteLine($"User:     {settings.UserName}");

        await using var connection = new NpgsqlConnection(settings.BuildConnectionString());
        await connection.OpenAsync(cancellationToken);

        Console.WriteLine("PostgreSQL connection: OK");

        var schemaExists = await SchemaExistsAsync(connection, cancellationToken);
        Console.WriteLine($"damper schema: {(schemaExists ? "FOUND" : "NOT FOUND")}");

        if (!schemaExists)
        {
            return;
        }

        var versionTableExists = await SchemaVersionTableExistsAsync(connection, cancellationToken);

        Console.WriteLine($"damper.schema_version: {(versionTableExists ? "FOUND" : "NOT FOUND")}");

        if (!versionTableExists)
        {
            return;
        }

        var version = await GetCurrentSchemaVersionAsync(connection, cancellationToken);

        Console.WriteLine(version.HasValue
                          ? $"Current schema version: {version.Value}"
                          : "Current schema version: NONE");
    }

    private static async Task<bool> SchemaExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.schemata
                WHERE schema_name = 'damper'
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<bool> SchemaVersionTableExistsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT EXISTS (
                SELECT 1
                FROM information_schema.tables
                WHERE table_schema = 'damper'
                  AND table_name = 'schema_version'
            );
            """;

        await using var command = new NpgsqlCommand(sql, connection);

        return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false);
    }

    private static async Task<int?> GetCurrentSchemaVersionAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT MAX(version)
            FROM damper.schema_version;
            """;

        await using var command = new NpgsqlCommand(sql, connection);

        var result = await command.ExecuteScalarAsync(cancellationToken);

        return result is DBNull or null
               ? null
               : Convert.ToInt32(result);
    }
}