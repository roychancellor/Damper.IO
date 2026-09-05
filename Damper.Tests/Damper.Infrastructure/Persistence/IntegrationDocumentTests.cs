using Damper.Domain.Common;
using Damper.Domain.Enums;
using Damper.Domain.Integrations;
using Damper.Domain.Integrations.OutAuthentication;
using Damper.Infrastructure.Persistence.PostgreSql;
using Damper.Infrastructure.Security;

namespace Damper.Tests;

[TestClass]
public class IntegrationDocumentTests
{
    private const string TEST_SECRET = "this-is-a-secret";

    [TestMethod]
    public void FromDomain_ToJson_FromJson_ToDomain_ShouldPassIf_AllValuesRoundTrip()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector();
        var integration = CreateIntegration();

        var document = IntegrationDocument.FromDomain(integration, protector);
        var json = document.ToJson();
        var deserializedDocument = IntegrationDocument.FromJson(json);

        var record = CreateRecord(integration);
        var result = deserializedDocument.ToDomain(record, protector);

        Assert.AreEqual(integration.Id, result.Id);
        Assert.AreEqual(integration.Name, result.Name);
        Assert.AreEqual(integration.Description, result.Description);
        Assert.AreEqual(integration.Enabled, result.Enabled);
        Assert.AreEqual(integration.CreatedUtc, result.CreatedUtc);
        Assert.AreEqual(integration.ModifiedUtc, result.ModifiedUtc);

        Assert.AreEqual(integration.Ingress.Enabled, result.Ingress.Enabled);
        CollectionAssert.AreEqual(integration.Ingress.ApiKeyHash.ToArray(), result.Ingress.ApiKeyHash.ToArray());

        Assert.AreEqual(integration.Delivery.Enabled, result.Delivery.Enabled);
        Assert.AreEqual(integration.Delivery.Destination.Uri, result.Delivery.Destination.Uri);

        Assert.AreEqual(integration.Delivery.Settings.RequestsPerInterval, result.Delivery.Settings.RequestsPerInterval);
        Assert.AreEqual(integration.Delivery.Settings.DeliveryIntervalMillis, result.Delivery.Settings.DeliveryIntervalMillis);
        Assert.AreEqual(integration.Delivery.Settings.MaxRetryAttempts, result.Delivery.Settings.MaxRetryAttempts);
        Assert.AreEqual(integration.Delivery.Settings.InitialRetryDelayMillis, result.Delivery.Settings.InitialRetryDelayMillis);
        Assert.AreEqual(integration.Delivery.Settings.RetryBackoffMultiplier, result.Delivery.Settings.RetryBackoffMultiplier);
        Assert.AreEqual(integration.Delivery.Settings.MaximumRetryDelayMillis, result.Delivery.Settings.MaximumRetryDelayMillis);
        Assert.AreEqual(integration.Delivery.Settings.RequestTimeoutMillis, result.Delivery.Settings.RequestTimeoutMillis);
        Assert.AreEqual(integration.Delivery.Settings.MaxQueueCapacity, result.Delivery.Settings.MaxQueueCapacity);

        Assert.AreEqual(integration.Delivery.Headers.Headers.Count, result.Delivery.Headers.Headers.Count);

        foreach (var header in integration.Delivery.Headers.Headers)
        {
            Assert.AreEqual(header.Value, result.Delivery.Headers.Headers[header.Key]);
        }

        Assert.IsInstanceOfType<BearerAuthentication>(result.Delivery.Authentication);

        var authentication = (BearerAuthentication)result.Delivery.Authentication;
        Assert.AreEqual(TEST_SECRET, authentication.Token.Reveal());
    }

    [TestMethod]
    public void ToJson_ShouldPassIf_PlaintextSecretIsNotPersisted()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector();
        var integration = CreateIntegration();

        var document = IntegrationDocument.FromDomain(integration, protector);
        var json = document.ToJson();

        Assert.IsFalse(json.Contains(TEST_SECRET, StringComparison.Ordinal));
    }

    [TestMethod]
    public void FromDomain_ShouldPassIf_ProtectedSecretIsCreated()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector();
        var integration = CreateIntegration();

        var document = IntegrationDocument.FromDomain(integration, protector);

        Assert.AreEqual(AuthenticationType.Bearer, document.Authentication.Type);
        Assert.IsNotNull(document.Authentication.Secret);
        Assert.IsNotEmpty(document.Authentication.Secret.Ciphertext);
        Assert.IsNotEmpty(document.Authentication.Secret.Nonce);
        Assert.IsNotEmpty(document.Authentication.Secret.Tag);
        Assert.AreEqual(1, document.Authentication.Secret.KeyVersion);
    }

    [TestMethod]
    public void FromDomain_ToDomain_ShouldPassIf_HeadersRoundTrip()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector();
        var integration = CreateIntegration();

        var document = IntegrationDocument.FromDomain(integration, protector);
        var result = document.ToDomain(CreateRecord(integration), protector);

        Assert.AreEqual(2, result.Delivery.Headers.Headers.Count);
        Assert.AreEqual("Alpha", result.Delivery.Headers.Headers["X-Test-One"]);
        Assert.AreEqual("Bravo", result.Delivery.Headers.Headers["X-Test-Two"]);
    }

    [TestMethod]
    public void Authentication_Basic_ShouldPassIf_RoundTrips()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector();
        var integration = CreateIntegration(new BasicAuthentication
        {
            Username = "fred",
            Password = new Secret(TEST_SECRET)
        });

        var document = IntegrationDocument.FromDomain(integration, protector);
        var result = document.ToDomain(CreateRecord(integration), protector);

        Assert.IsInstanceOfType<BasicAuthentication>(result.Delivery.Authentication);

        var authentication = (BasicAuthentication)result.Delivery.Authentication;

        Assert.AreEqual("fred", authentication.Username);
        Assert.AreEqual(TEST_SECRET, authentication.Password.Reveal());
    }

    [TestMethod]
    public void Authentication_CustomHeader_ShouldPassIf_RoundTrips()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector();
        var integration = CreateIntegration(new CustomHeaderAuthentication
        {
            HeaderName = "X-API-Key",
            HeaderValue = new Secret(TEST_SECRET)
        });

        var document = IntegrationDocument.FromDomain(integration, protector);
        var result = document.ToDomain(CreateRecord(integration), protector);

        Assert.IsInstanceOfType<CustomHeaderAuthentication>(result.Delivery.Authentication);

        var authentication = (CustomHeaderAuthentication)result.Delivery.Authentication;

        Assert.AreEqual("X-API-Key", authentication.HeaderName);
        Assert.AreEqual(TEST_SECRET, authentication.HeaderValue.Reveal());
    }

    [TestMethod]
    public void Authentication_None_ShouldPassIf_RoundTrips()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector();
        var integration = CreateIntegration(new NoAuthentication());

        var document = IntegrationDocument.FromDomain(integration, protector);
        var result = document.ToDomain(CreateRecord(integration), protector);

        Assert.IsInstanceOfType<NoAuthentication>(result.Delivery.Authentication);
    }

    private static Integration CreateIntegration(OutboundAuthentication? authentication = null)
    {
        return new Integration
        {
            Id = 123,
            Name = new IntegrationName("Test Integration"),
            Description = "IntegrationDocument persistence test",
            Enabled = true,

            Ingress = new Ingress
            {
                Enabled = true,
                ApiKeyHash = new ApiKeyHash(Enumerable.Range(1, 32).Select(i => (byte)i).ToArray())
            },

            Delivery = new Delivery
            {
                Enabled = true,

                Destination = new Destination
                {
                    Uri = new Uri("https://example.com/webhook")
                },

                Settings = new DeliverySettings
                {
                    RequestsPerInterval = 5,
                    DeliveryIntervalMillis = 1000,
                    MaxRetryAttempts = 4,
                    InitialRetryDelayMillis = 500,
                    RetryBackoffMultiplier = 2.0,
                    MaximumRetryDelayMillis = 30000,
                    RequestTimeoutMillis = 10000,
                    MaxQueueCapacity = 5000
                },

                Authentication = authentication ?? new BearerAuthentication
                {
                    Token = new Secret(TEST_SECRET)
                },

                Headers = new HeaderCollection
                {
                    Headers = new Dictionary<string, string>
                    {
                        ["X-Test-One"] = "Alpha",
                        ["X-Test-Two"] = "Bravo"
                    }
                }
            },

            CreatedUtc = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            ModifiedUtc = new DateTimeOffset(2026, 2, 3, 4, 5, 6, TimeSpan.Zero)
        };
    }

    private static IntegrationRecord CreateRecord(Integration integration)
    {
        return new IntegrationRecord
        {
            Id = integration.Id,
            Name = integration.Name.ToString(),
            Enabled = integration.Enabled,
            ApiKeyHash = integration.Ingress.ApiKeyHash.ToArray(),
            CreatedAt = integration.CreatedUtc,
            ModifiedAt = integration.ModifiedUtc
        };
    }
}