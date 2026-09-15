using RabbitMQ.Client;

namespace Damper.Tests.IntegrationTests.RabbitMq;

[TestClass]
public sealed class RabbitMqBootstrapTests
{
    [TestMethod]
    public async Task RabbitMqContainer_StartsAndAcceptsConnection()
    {
        await using var environment = new RabbitMqTestEnvironment();

        await environment.StartAsync(TestContext.CancellationToken);

        await using var connection = await environment.CreateConnectionAsync(TestContext.CancellationToken);

        Assert.IsTrue(connection.IsOpen);
    }

    public TestContext TestContext { get; set; } = null!;
}