using Damper.Application.Integrations;
using Damper.Domain.Integrations;
using Damper.Tests.IntegrationTests.PostgreSql;
using Damper.Tests.IntegrationTests.RabbitMq;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Damper.Tests.EndToEnd;

internal sealed class DamperEndToEndEnvironment : IAsyncDisposable
{
    private readonly PostgreSqlTestEnvironment _postgres = new();
    private readonly RabbitMqTestEnvironment _rabbitMq = new();

    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;

    public HttpClient Client => _client
                                ?? throw new InvalidOperationException("The E2E environment has not been started.");

    public IServiceProvider Services => _factory?.Services
                                        ?? throw new InvalidOperationException("The E2E environment has not been started.");

    public async Task StartAsync()
    {
        await _postgres.StartAsync();
        await _rabbitMq.StartAsync();

        var repositorySettings = _postgres.GetRuntimeRepositorySettings();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Testing");

                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["SeedDevelopmentData"] = "false",

                        ["ApplicationData:RepositorySettings:HostName"] = repositorySettings.HostName,
                        ["ApplicationData:RepositorySettings:Port"] = repositorySettings.Port.ToString(),
                        ["ApplicationData:RepositorySettings:Database"] = repositorySettings.Database,
                        ["ApplicationData:RepositorySettings:UserName"] = repositorySettings.UserName,
                        ["ApplicationData:RepositorySettings:Password"] = repositorySettings.Password,

                        ["ApplicationData:RabbitMQSettings:HostName"] = _rabbitMq.HostName,
                        ["ApplicationData:RabbitMQSettings:Port"] = _rabbitMq.Port.ToString(),
                        ["ApplicationData:RabbitMQSettings:UserName"] = _rabbitMq.User,
                        ["ApplicationData:RabbitMQSettings:Password"] = _rabbitMq.Pass,

                        ["ApplicationData:EncryptionSettings:Key"] = Convert.ToBase64String(new byte[32]),
                        ["ApplicationData:EncryptionSettings:KeyVersion"] = "1"
                    });
                });
            });

        // CreateClient causes WebApplicationFactory to actually boot Damper,
        // including all hosted shard workers.
        _client = _factory.CreateClient();
    }

    public async Task ResetAsync()
    {
        await _postgres.ResetAsync();
    }

    public async ValueTask DisposeAsync()
    {
        // Stop Damper and its RabbitMQ workers before destroying
        // the infrastructure they are connected to.
        _client?.Dispose();
        _factory?.Dispose();

        await _rabbitMq.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    public async Task<Integration> SaveIntegrationAsync(Integration integration, CancellationToken cancellationToken = default)
    {
        using var scope = Services.CreateScope();

        var service = scope.ServiceProvider.GetRequiredService<IIntegrationService>();

        return await service.SaveAsync(integration, cancellationToken);
    }

    public async Task WaitForDeadLetterAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        var expires = DateTimeOffset.UtcNow + timeout;

        while (DateTimeOffset.UtcNow < expires)
        {
            if (await _rabbitMq.GetMessageCountAsync(RabbitMqTestEnvironment.DeadLetterQueue, cancellationToken) > 0)
            {
                return;
            }

            await Task.Delay(50, cancellationToken);
        }

        throw new TimeoutException($"No message appeared in the DLQ within {timeout}.");
    }
}