using Damper.Domain.Common;
using Damper.Domain.Integrations;
using Damper.Domain.Integrations.OutAuthentication;
using Damper.Infrastructure.Persistence.PostgreSql;
using Damper.Infrastructure.ReferenceData;
using Damper.Infrastructure.Security;
using Damper.Tests.IntegrationTests.PostgreSql;
using Dapper;
using Microsoft.Extensions.Options;
using Npgsql;
using System.Security.Cryptography;

namespace Damper.Tests.Damper.Infrastructure.Persistence;

[TestClass]
[DoNotParallelize]
public sealed class PostgreSqlIntegrationRepositoryTests
{
    private static PostgreSqlTestEnvironment _environment = null!;
    private static PostgreSqlIntegrationRepository _repository = null!;

    private const string TestSecret = "integration-test-secret";

    #region Class and Test Level Initialize and Cleanup
    public TestContext TestContext { get; set; } = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _environment = new PostgreSqlTestEnvironment();
        await _environment.StartAsync();

        var repositoryOptions = Options.Create(_environment.GetRuntimeRepositorySettings());

        var encryptionSettings = new EncryptionSettings
        {
            Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            KeyVersion = 1
        };

        var secretProtector = new AesGcmSecretProtector(Options.Create(encryptionSettings));

        _repository = new PostgreSqlIntegrationRepository(repositoryOptions, secretProtector);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _environment.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await _environment.ResetAsync();
    }
    #endregion

    #region Integration Tests
    [TestMethod]
    public async Task SaveAsync_NewIntegration_ReturnsGeneratedId()
    {
        var integration = CreateIntegration();

        var saved = await _repository.SaveAsync(integration, TestContext.CancellationToken);

        Assert.IsGreaterThan(0, saved.Id);
    }

    [TestMethod]
    public async Task SaveAsync_NewIntegration_ReturnsDatabaseTimestamps()
    {
        var integration = CreateIntegration();

        var saved = await _repository.SaveAsync(integration, TestContext.CancellationToken);

        Assert.AreNotEqual(default, saved.CreatedUtc);
        Assert.AreNotEqual(default, saved.ModifiedUtc);
        Assert.IsLessThanOrEqualTo(saved.ModifiedUtc, saved.CreatedUtc);
    }

    [TestMethod]
    public async Task GetByIdAsync_ReturnsSavedIntegration()
    {
        var integration = CreateIntegration();
        var saved = await _repository.SaveAsync(integration, TestContext.CancellationToken);

        var result = await _repository.GetByIdAsync(saved.Id, TestContext.CancellationToken);

        Assert.IsNotNull(result);
        Assert.AreEqual(saved.Id, result.Id);
        Assert.AreEqual(saved.Name, result.Name);
        Assert.AreEqual(saved.Description, result.Description);
        Assert.AreEqual(saved.Enabled, result.Enabled);
        Assert.AreEqual(saved.Ingress.Enabled, result.Ingress.Enabled);
        Assert.AreSequenceEqual(saved.Ingress.ApiKeyHash.ToArray(), result.Ingress.ApiKeyHash.ToArray());
        Assert.AreEqual(saved.Delivery.Enabled, result.Delivery.Enabled);
        Assert.AreEqual(saved.Delivery.Destination.Uri, result.Delivery.Destination.Uri);
    }

    [TestMethod]
    public async Task GetByApiKeyHashAsync_ReturnsEnabledIntegration()
    {
        var integration = CreateIntegration(enabled: true);
        var saved = await _repository.SaveAsync(integration, TestContext.CancellationToken);

        var result = await _repository.GetByApiKeyHashAsync(saved.Ingress.ApiKeyHash, TestContext.CancellationToken);

        Assert.IsNotNull(result);
        Assert.AreEqual(saved.Id, result.Id);
        Assert.AreEqual(saved.Name, result.Name);
    }

    [TestMethod]
    public async Task GetByApiKeyHashAsync_DoesNotReturnDisabledIntegration()
    {
        var integration = CreateIntegration(enabled: false);
        var saved = await _repository.SaveAsync(integration, TestContext.CancellationToken);

        var result = await _repository.GetByApiKeyHashAsync(saved.Ingress.ApiKeyHash, TestContext.CancellationToken);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task SaveAsync_ExistingIntegration_UpdatesIntegration()
    {
        var original = CreateIntegration();
        var saved = await _repository.SaveAsync(original, TestContext.CancellationToken);

        var updated = new Integration
        {
            Id = saved.Id,
            Name = saved.Name,
            Description = "Updated description",
            Enabled = false,
            Ingress = saved.Ingress,

            Delivery = new Delivery
            {
                Enabled = false,
                Destination = saved.Delivery.Destination,
                Settings = saved.Delivery.Settings,
                Authentication = saved.Delivery.Authentication,
                Headers = saved.Delivery.Headers
            },

            CreatedUtc = saved.CreatedUtc,
            ModifiedUtc = saved.ModifiedUtc
        };

        var result = await _repository.SaveAsync(updated, TestContext.CancellationToken);

        Assert.AreEqual(saved.Id, result.Id);
        Assert.AreEqual("Updated description", result.Description);
        Assert.IsFalse(result.Enabled);
        Assert.IsFalse(result.Delivery.Enabled);
        Assert.AreEqual(saved.CreatedUtc, result.CreatedUtc);
        Assert.IsGreaterThanOrEqualTo(saved.ModifiedUtc, result.ModifiedUtc);
    }

    [TestMethod]
    public async Task GetAllAsync_ReturnsIntegrations()
    {
        var first = await _repository.SaveAsync(CreateIntegration(), TestContext.CancellationToken);
        var second = await _repository.SaveAsync(CreateIntegration(), TestContext.CancellationToken);
        var third = await _repository.SaveAsync(CreateIntegration(), TestContext.CancellationToken);

        var results = await _repository.GetAllAsync(TestContext.CancellationToken);

        Assert.HasCount(3, results);
        Assert.Contains(x => x.Id == first.Id, results);
        Assert.Contains(x => x.Id == second.Id, results);
        Assert.Contains(x => x.Id == third.Id, results);
    }

    [TestMethod]
    public async Task DeleteAsync_RemovesIntegration()
    {
        var saved = await _repository.SaveAsync(CreateIntegration(), TestContext.CancellationToken);

        await _repository.DeleteAsync(saved.Id, TestContext.CancellationToken);

        var result = await _repository.GetByIdAsync(saved.Id, TestContext.CancellationToken);

        Assert.IsNull(result);
    }

    [TestMethod]
    public async Task SaveAsync_DuplicateApiKeyHash_Throws()
    {
        var apiKeyHash = new ApiKeyHash(RandomNumberGenerator.GetBytes(32));

        await _repository.SaveAsync(CreateIntegration(apiKeyHash: apiKeyHash), TestContext.CancellationToken);

        var exception = await Assert.ThrowsExactlyAsync<PostgresException>(
            () => _repository.SaveAsync(CreateIntegration(apiKeyHash: apiKeyHash), TestContext.CancellationToken));

        Assert.AreEqual(PostgresErrorCodes.UniqueViolation, exception.SqlState);
    }

    [TestMethod]
    public async Task SaveAsync_DoesNotStorePlaintextSecret()
    {
        var saved = await _repository.SaveAsync(CreateIntegration(), TestContext.CancellationToken);

        await using var connection = new NpgsqlConnection(_environment.GetAdminConnectionString());
        await connection.OpenAsync(TestContext.CancellationToken);

        await using var command = new NpgsqlCommand(
            "SELECT configuration::text FROM damper.integration WHERE id = @id;",
            connection);

        command.Parameters.AddWithValue("id", saved.Id);

        var configuration = (string?)await command.ExecuteScalarAsync(TestContext.CancellationToken);

        Assert.IsNotNull(configuration);
        Assert.DoesNotContain(TestSecret, configuration);
    }

    [TestMethod]
    public async Task SaveAsync_BasicAuthentication_RoundTrips()
    {
        var integration = CreateIntegration(authentication: new BasicAuthentication
        {
            Username = "test-user",
            Password = new Secret(TestSecret)
        });

        var saved = await _repository.SaveAsync(integration, TestContext.CancellationToken);
        var result = await _repository.GetByIdAsync(saved.Id, TestContext.CancellationToken);

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<BasicAuthentication>(result.Delivery.Authentication);

        var authentication = (BasicAuthentication)result.Delivery.Authentication;

        Assert.AreEqual("test-user", authentication.Username);
        Assert.AreEqual(TestSecret, authentication.Password.Reveal());
    }

    [TestMethod]
    public async Task SaveAsync_CustomHeaderAuthentication_RoundTrips()
    {
        var integration = CreateIntegration(authentication: new CustomHeaderAuthentication
        {
            HeaderName = "X-API-Key",
            HeaderValue = new Secret(TestSecret)
        });

        var saved = await _repository.SaveAsync(integration, TestContext.CancellationToken);
        var result = await _repository.GetByIdAsync(saved.Id, TestContext.CancellationToken);

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<CustomHeaderAuthentication>(result.Delivery.Authentication);

        var authentication = (CustomHeaderAuthentication)result.Delivery.Authentication;

        Assert.AreEqual("X-API-Key", authentication.HeaderName);
        Assert.AreEqual(TestSecret, authentication.HeaderValue.Reveal());
    }
    #endregion

    #region Helper Methods
    private static Integration CreateIntegration(bool enabled = true, OutboundAuthentication? authentication = null, ApiKeyHash? apiKeyHash = null)
    {
        return new Integration
        {
            Name = new IntegrationName($"Integration Test {Guid.NewGuid():N}"),
            Description = "PostgreSQL repository integration test",
            Enabled = enabled,

            Ingress = new Ingress
            {
                Enabled = true,
                ApiKeyHash = apiKeyHash ?? new ApiKeyHash(RandomNumberGenerator.GetBytes(32))
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
                    Token = new Secret(TestSecret)
                },
                Headers = new HeaderCollection
                {
                    Headers = new Dictionary<string, string>
                    {
                        ["X-Test-One"] = "Alpha",
                        ["X-Test-Two"] = "Bravo"
                    }
                }
            }
        };
    }
    #endregion
}