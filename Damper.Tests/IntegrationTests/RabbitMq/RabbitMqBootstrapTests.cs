using RabbitMQ.Client;

namespace Damper.Tests.IntegrationTests.RabbitMq;

[TestClass]
public sealed class RabbitMqBootstrapTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task RabbitMqContainer_StartsAndAcceptsConnection()
    {
        await using var environment = new RabbitMqTestEnvironment();

        await environment.StartAsync(TestContext.CancellationToken);

        await using var connection = await environment.CreateConnectionAsync(TestContext.CancellationToken);

        Assert.IsTrue(connection.IsOpen);
    }

    [TestMethod]
    public async Task RabbitMqContainer_ProvisionsDamperTopology()
    {
        await using var environment = new RabbitMqTestEnvironment();

        await environment.StartAsync(TestContext.CancellationToken);

        await using var connection = await environment.CreateConnectionAsync(TestContext.CancellationToken);

        await using var channel = await connection.CreateChannelAsync(cancellationToken: TestContext.CancellationToken);

        await channel.ExchangeDeclarePassiveAsync(
            RabbitMqTestEnvironment.MessageExchange,
            cancellationToken: TestContext.CancellationToken);

        await channel.ExchangeDeclarePassiveAsync(
            RabbitMqTestEnvironment.DeadLetterExchange,
            cancellationToken: TestContext.CancellationToken);

        await channel.ExchangeDeclarePassiveAsync(
            RabbitMqTestEnvironment.ParkingExchange,
            cancellationToken: TestContext.CancellationToken);

        await channel.QueueDeclarePassiveAsync(
            RabbitMqTestEnvironment.DeadLetterQueue,
            cancellationToken: TestContext.CancellationToken);

        await channel.QueueDeclarePassiveAsync(
            RabbitMqTestEnvironment.ParkingQueue,
            cancellationToken: TestContext.CancellationToken);

        for (var i = 0; i < RabbitMqTestEnvironment.ShardCount; i++)
        {
            await channel.QueueDeclarePassiveAsync(
                $"{RabbitMqTestEnvironment.ShardPrefix}{i:D2}",
                cancellationToken: TestContext.CancellationToken);
        }
    }
}