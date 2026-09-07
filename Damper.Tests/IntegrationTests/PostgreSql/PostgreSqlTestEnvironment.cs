using Damper.Infrastructure.ReferenceData;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Damper.Tests.IntegrationTests.PostgreSql;

internal sealed class PostgreSqlTestEnvironment : IAsyncDisposable
{
    private const string Database = "damper";

    private const string Superuser = "damper-superuser";
    private const string SuperuserPassword = "damper-superuser-test-password";

    private const string AdminUser = "damper-admin";
    private const string AdminPassword = "damper-admin-test-password";

    private const string RuntimeUser = "damper-runtime";
    private const string RuntimePassword = "damper-runtime-test-password";

    private readonly PostgreSqlContainer _container;

    public PostgreSqlTestEnvironment()
    {
        var initDirectory = Path.Combine(AppContext.BaseDirectory, "postgres-init");

        var rolesScript = Path.Combine(initDirectory, "01-roles.sh");
        var schemaScript = Path.Combine(initDirectory, "02-schema.sh");

        if (!File.Exists(rolesScript))
        {
            throw new FileNotFoundException("PostgreSQL role initialization script was not found.", rolesScript);
        }

        if (!File.Exists(schemaScript))
        {
            throw new FileNotFoundException("PostgreSQL schema initialization script was not found.", schemaScript);
        }

        _container = new PostgreSqlBuilder("postgres:18")
            .WithDatabase(Database)
            .WithUsername(Superuser)
            .WithPassword(SuperuserPassword)
            .WithEnvironment("DAMPER_ADMIN_USER", AdminUser)
            .WithEnvironment("DAMPER_ADMIN_PASSWORD", AdminPassword)
            .WithEnvironment("DAMPER_RUNTIME_USER", RuntimeUser)
            .WithEnvironment("DAMPER_RUNTIME_PASSWORD", RuntimePassword)
            .WithResourceMapping(rolesScript, "/docker-entrypoint-initdb.d/")
            .WithResourceMapping(schemaScript, "/docker-entrypoint-initdb.d/")
            .Build();
    }

    public async Task ResetAsync()
    {
        await using var connection = new NpgsqlConnection(GetAdminConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("TRUNCATE TABLE damper.integration RESTART IDENTITY;", connection);

        await command.ExecuteNonQueryAsync();
    }

    public async Task StartAsync()
    {
        await _container.StartAsync();
    }

    public RepositorySettings GetRuntimeRepositorySettings()
    {
        return new RepositorySettings
        {
            HostName = _container.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = Database,
            UserName = RuntimeUser,
            Password = RuntimePassword
        };
    }

    public string GetRuntimeConnectionString()
    {
        return new NpgsqlConnectionStringBuilder
        {
            Host = _container.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = Database,
            Username = RuntimeUser,
            Password = RuntimePassword
        }.ConnectionString;
    }

    public string GetAdminConnectionString()
    {
        return new NpgsqlConnectionStringBuilder
        {
            Host = _container.Hostname,
            Port = _container.GetMappedPublicPort(5432),
            Database = Database,
            Username = AdminUser,
            Password = AdminPassword
        }.ConnectionString;
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}