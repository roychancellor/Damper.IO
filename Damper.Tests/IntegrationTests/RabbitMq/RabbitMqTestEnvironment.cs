using RabbitMQ.Client;
using Testcontainers.RabbitMq;

namespace Damper.Tests.IntegrationTests.RabbitMq;

internal sealed class RabbitMqTestEnvironment : IAsyncDisposable
{
    private const string UserName = "damper-test";
    private const string Password = "damper-test-password";

    public const string MessageExchange = "damper.message.exchange";
    public const string DeadLetterExchange = "damper.dlx";
    public const string DeadLetterQueue = "damper.message.queue.dead_letter";
    public const string ParkingExchange = "damper.parking.exchange";
    public const string ParkingQueue = "damper.message.queue.parking";
    public const string ShardPrefix = "damper.message.queue.shard_";
    public const int ShardCount = 16;

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

        var result = await _container.ExecAsync(
            ["rabbitmq-plugins", "enable", "rabbitmq_consistent_hash_exchange"],
            cancellationToken);

        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"Unable to enable RabbitMQ consistent-hash plugin: {result.Stderr}");
        }

        await ProvisionTopologyAsync(cancellationToken);
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

    private async Task ProvisionTopologyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            DeadLetterExchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            DeadLetterQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum"
            },
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            DeadLetterQueue,
            DeadLetterExchange,
            "dead-letter",
            cancellationToken: cancellationToken);

        await channel.ExchangeDeclareAsync(
            MessageExchange,
            "x-consistent-hash",
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        for (var i = 0; i < ShardCount; i++)
        {
            var queueName = $"{ShardPrefix}{i:D2}";

            await channel.QueueDeclareAsync(
                queueName,
                durable: true,
                exclusive: false,
                autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-queue-type"] = "quorum",
                    ["x-dead-letter-exchange"] = DeadLetterExchange,
                    ["x-dead-letter-routing-key"] = "dead-letter"
                },
                cancellationToken: cancellationToken);

            await channel.QueueBindAsync(
                queueName,
                MessageExchange,
                "1",
                cancellationToken: cancellationToken);
        }

        await channel.ExchangeDeclareAsync(
            ParkingExchange,
            ExchangeType.Fanout,
            durable: true,
            autoDelete: false,
            cancellationToken: cancellationToken);

        await channel.QueueDeclareAsync(
            ParkingQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-queue-type"] = "quorum",
                ["x-dead-letter-exchange"] = MessageExchange
            },
            cancellationToken: cancellationToken);

        await channel.QueueBindAsync(
            ParkingQueue,
            ParkingExchange,
            string.Empty,
            cancellationToken: cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _container.DisposeAsync();
    }

    public async Task<uint> GetMessageCountAsync(string queueName, CancellationToken cancellationToken = default)
    {
        await using var connection = await CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(cancellationToken: cancellationToken);

        var result = await channel.QueueDeclarePassiveAsync(
            queueName,
            cancellationToken: cancellationToken);

        return result.MessageCount;
    }
}