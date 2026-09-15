using Damper.Domain.Common;
using Damper.Domain.Integrations;
using Damper.Domain.Integrations.OutAuthentication;
using Damper.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using System.Net;
using System.Text;

namespace Damper.Tests.EndToEnd;

[TestClass]
[DoNotParallelize]
public sealed class DamperEndToEndTests
{
    public TestContext TestContext { get; set; } = null!;

    [TestMethod]
    public async Task Message_IsDelivered_EndToEnd()
    {
        const string apiKey = "E2E-HAPPY-PATH";
        const string payload = """{"message":"Hello from Damper E2E"}""";

        await using var damperEnvironment = new DamperEndToEndEnvironment();
        await using var destinationServer = new TestDestinationServer();

        // Start the destination first because we need the dynamically generated URL
        await destinationServer.StartAsync(TestContext.CancellationToken);
        
        await damperEnvironment.StartAsync();

        var integration = CreateIntegration(apiKey, destinationServer.Uri, enabled: true);

        await damperEnvironment.SaveIntegrationAsync(integration, TestContext.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inbound");
        request.Headers.Add("X-Damper-Api-Key", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await damperEnvironment.Client.SendAsync(request, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        var delivered = await destinationServer.WaitForRequestAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.AreEqual(payload, Encoding.UTF8.GetString(delivered.Body));

        Assert.AreEqual("application/json; charset=utf-8", delivered.ContentType);
    }

    [TestMethod]
    public async Task DisabledIntegration_IsRejected_AndNotDelivered()
    {
        const string apiKey = "E2E-DISABLED";
        const string payload = """{"message":"This should not be delivered"}""";

        await using var environment = new DamperEndToEndEnvironment();
        await using var destination = new TestDestinationServer();

        await destination.StartAsync(TestContext.CancellationToken);
        await environment.StartAsync();

        /////////////////////////////////////////////////////////////////////////////
        var integration = CreateIntegration(apiKey, destination.Uri, enabled: false);
        /////////////////////////////////////////////////////////////////////////////

        await environment.SaveIntegrationAsync(integration, TestContext.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inbound");
        request.Headers.Add("X-Damper-Api-Key", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await environment.Client.SendAsync(request, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.AreEqual(0, destination.RequestCount);
    }

    [TestMethod]
    public async Task DestinationReturns400_MessageIsDeadLettered()
    {
        const string apiKey = "E2E-DLQ";
        const string payload = """{"message":"Send me to the DLQ"}""";

        await using var environment = new DamperEndToEndEnvironment();
        await using var destination = new TestDestinationServer
        {
            ResponseStatusCode = StatusCodes.Status400BadRequest
        };

        await destination.StartAsync(TestContext.CancellationToken);
        await environment.StartAsync();

        var integration = CreateIntegration(apiKey, destination.Uri, enabled: true);

        await environment.SaveIntegrationAsync(integration, TestContext.CancellationToken);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/inbound");
        request.Headers.Add("X-Damper-Api-Key", apiKey);
        request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

        using var response = await environment.Client.SendAsync(request, TestContext.CancellationToken);

        Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);

        var delivered = await destination.WaitForRequestAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.AreEqual( payload, Encoding.UTF8.GetString(delivered.Body));

        await environment.WaitForDeadLetterAsync(TimeSpan.FromSeconds(10), TestContext.CancellationToken);

        Assert.AreEqual(1, destination.RequestCount);
    }

    private static Integration CreateIntegration( string apiKey, Uri destination, bool enabled)
    {
        return new Integration
        {
            Name = new IntegrationName($"E2E {apiKey}"),
            Description = "End-to-end test integration",
            Enabled = enabled,

            Ingress = new Ingress
            {
                Enabled = true,
                ApiKeyHash = new ApiKey(apiKey).ToHash()
            },

            Delivery = new Delivery
            {
                Enabled = true,
                Destination = new Destination
                {
                    Uri = destination
                },
                Authentication = new NoAuthentication(),
                Headers = new HeaderCollection(),

                Settings = new DeliverySettings
                {
                    RequestsPerInterval = 100,
                    DeliveryIntervalMillis = 10,
                    MaxRetryAttempts = 3,
                    InitialRetryDelayMillis = 10,
                    RetryBackoffMultiplier = 2,
                    MaximumRetryDelayMillis = 100,
                    RequestTimeoutMillis = 5000,
                    MaxQueueCapacity = 100
                }
            }
        };
    }
}