using System.Net;

namespace Damper.Tests.EndToEnd;

[TestClass]
public sealed class DamperEndToEndBootstrapTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Damper_StartsAgainstEphemeralInfrastructure()
    {
        await using var environment = new DamperEndToEndEnvironment();

        await environment.StartAsync();

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inbound");
        using var response = await environment.Client.SendAsync(request, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}