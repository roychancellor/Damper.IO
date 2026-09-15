using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Damper.Tests.IntegrationTests.RabbitMq;

internal sealed class RabbitMqTestEnvironment : IAsyncDisposable
{
    private const string UserName = "damper-test";
    private const string Password = "damper-test-password";

    private readonly RabbitMqContainer _container;

    public RabbitMqTestEnvironment()
    {
        _container = new RabbitMqBuilder("rabbitmq:4-management")
            .WithUsername(UserName)
            .WithPassword(Password)
            .Build();
    }

    public string HostName => _container.Hostname;
    public int Port => _container.GetMappedPublicPort(5672);
    public string User => UserName;
    public string Pass => Password;

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        await _container.StartAsync(cancellationToken);
    }

    public async Task<IConnection> CreateConnectionAsync(CancellationToken cancellationToken = default)
    {
        var factory = new ConnectionFactory
        {
            HostName = HostName,
            Port = Port,
            UserName = UserName,
            Password = Password
        };

        return await factory.CreateConnectionAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }
}