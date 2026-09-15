using Damper.Application.Integrations;
using Damper.Domain.Common;
using Damper.Domain.Integrations;
using Damper.Infrastructure.ReferenceData;
using Damper.Infrastructure.Security;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;

namespace Damper.Infrastructure.Persistence.PostgreSql;

public sealed class PostgreSqlIntegrationRepository : IIntegrationRepository
{
    private readonly string _connectionString;
    private readonly ISecretProtector _secretProtector;

    private NpgsqlConnection CreateConnection() => new(_connectionString);

    private const string GetByIdSql = "SELECT * FROM damper.integration_get_by_id(@p_id);";
    private const string GetByApiKeyHashSql = "SELECT * FROM damper.integration_get_by_api_key_hash(@p_api_key_hash);";
    private const string GetAllSql = "SELECT * FROM damper.integration_get_all();";
    private const string InsertSql = "SELECT * FROM damper.integration_insert(@p_name, @p_enabled, @p_api_key_hash, CAST(@p_configuration AS jsonb));";
    private const string UpdateSql = "SELECT * FROM damper.integration_update(@p_id, @p_name, @p_enabled, @p_api_key_hash, CAST(@p_configuration AS jsonb));";
    private const string DeleteByIdSql = "SELECT damper.integration_delete(@p_id);";

    public PostgreSqlIntegrationRepository(IOptions<AppSettings> options, ISecretProtector secretProtector)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(secretProtector);

        var settings = options.Value.RepositorySettings;

        _connectionString = new NpgsqlConnectionStringBuilder
        {
            Host = settings.HostName,
            Port = settings.Port,
            Database = settings.Database,
            Username = settings.UserName,
            Password = settings.Password
        }.ConnectionString;

        _secretProtector = secretProtector;
    }

    public async Task<Integration?> GetByIdAsync(long integrationId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();

        var command = new CommandDefinition(GetByIdSql, new { p_id = integrationId }, cancellationToken: cancellationToken);

        var record = await connection.QuerySingleOrDefaultAsync<IntegrationRecord>(command);

        if (record is null)
        {
            return null;
        }

        var document = IntegrationDocument.FromJson(record.Configuration);

        return document.ToDomain(record, _secretProtector);
    }

    public async Task<Integration?> GetByApiKeyHashAsync(ApiKeyHash apiKeyHash, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();

        var command = new CommandDefinition(GetByApiKeyHashSql, new { p_api_key_hash = apiKeyHash.ToArray() }, cancellationToken: cancellationToken);

        var record = await connection.QuerySingleOrDefaultAsync<IntegrationRecord>(command);

        if (record is null)
        {
            return null;
        }

        var document = IntegrationDocument.FromJson(record.Configuration);

        return document.ToDomain(record, _secretProtector);
    }

    public async Task<IReadOnlyCollection<Integration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();

        var command = new CommandDefinition(GetAllSql, cancellationToken: cancellationToken);

        var records = await connection.QueryAsync<IntegrationRecord>(command);

        return [.. records
                .Select(record =>
                {
                    var document = IntegrationDocument.FromJson(record.Configuration);
                    return document.ToDomain(record, _secretProtector);
                })];
    }

    public async Task<Integration> SaveAsync(Integration integration, CancellationToken cancellationToken = default)
    {
        var document = IntegrationDocument.FromDomain(integration, _secretProtector);
        var configuration = document.ToJson();

        return integration.Id == 0
                ? await InsertAsync(integration, configuration, cancellationToken)
                : await UpdateAsync(integration, configuration, cancellationToken);
    }

    public async Task DeleteAsync(long integrationId, CancellationToken cancellationToken = default)
    {
        await using var connection = CreateConnection();

        var command = new CommandDefinition(DeleteByIdSql, new { p_id = integrationId }, cancellationToken: cancellationToken);

        await connection.ExecuteScalarAsync<long?>(command);
    }

    private async Task<Integration> InsertAsync(Integration integration, string configuration, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();

        var command = new CommandDefinition(
            InsertSql,
            new
            {
                p_name = integration.Name.ToString(),
                p_enabled = integration.Enabled,
                p_api_key_hash = integration.Ingress.ApiKeyHash.ToArray(),
                p_configuration = configuration
            },
            cancellationToken: cancellationToken);

        var record = await connection.QuerySingleAsync<IntegrationRecord>(command);
        var document = IntegrationDocument.FromJson(record.Configuration);

        return document.ToDomain(record, _secretProtector);
    }

    private async Task<Integration> UpdateAsync(Integration integration, string configuration, CancellationToken cancellationToken)
    {
        await using var connection = CreateConnection();

        var command = new CommandDefinition(
            UpdateSql,
            new
            {
                p_id = integration.Id,
                p_name = integration.Name.ToString(),
                p_enabled = integration.Enabled,
                p_api_key_hash = integration.Ingress.ApiKeyHash.ToArray(),
                p_configuration = configuration
            },
            cancellationToken: cancellationToken);

        var record = await connection.QuerySingleOrDefaultAsync<IntegrationRecord>(command)
                  ?? throw new InvalidOperationException($"Integration {integration.Id} does not exist.");
        
        var document = IntegrationDocument.FromJson(record.Configuration);
        return document.ToDomain(record, _secretProtector);
    }
}