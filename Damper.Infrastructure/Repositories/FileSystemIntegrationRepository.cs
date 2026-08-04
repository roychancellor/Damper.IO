using Damper.Domain.Common;
using Damper.Domain.Integrations;
using Damper.Infrastructure.Security;

namespace Damper.Infrastructure.Repositories;

public class FileSystemIntegrationRepository : IIntegrationRepository
{
    private static readonly List<Integration> _integrations =
    [
        BuildIntegration(1, apiKey: "HTTP200", destHttpReturnCode: 200),
        BuildIntegration(2, apiKey: "HTTP202", destHttpReturnCode: 202),
        BuildIntegration(3, apiKey: "HTTP301", destHttpReturnCode: 301),
        BuildIntegration(4, apiKey: "HTTP400", destHttpReturnCode: 400),
        BuildIntegration(5, apiKey: "HTTP401", destHttpReturnCode: 401),
        BuildIntegration(6, apiKey: "HTTP403", destHttpReturnCode: 403),
        BuildIntegration(7, apiKey: "HTTP429", destHttpReturnCode: 429),
        BuildIntegration(8, apiKey: "HTTP500", destHttpReturnCode: 500),
    ];

    public async Task DeleteAsync(long integrationId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyCollection<Integration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<Integration?> GetByApiKeyHashAsync(ApiKeyHash apiKeyHash, CancellationToken cancellationToken = default)
    {
        // TODO: Actually go get the repository objects from file system
        var toReturn = _integrations.FirstOrDefault(i => i.Ingress.ApiKeyHash.Equals(apiKeyHash));
        return await Task.FromResult(toReturn);
    }

    public async Task<Integration?> GetByIdAsync(long integrationId, CancellationToken cancellationToken = default)
    {
        // TODO: Actually go get the repository objects from file system
        var toReturn = _integrations.FirstOrDefault(i => i.Id == integrationId);
        return await Task.FromResult(toReturn);
    }

    public async Task SaveAsync(Integration integration, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    private static Integration BuildIntegration(long id, string apiKey, int destHttpReturnCode)
    {
        return new Integration
        { 
            Id = id,
            Name = new($"{apiKey} Integration"),
            Description = $"Testing {apiKey} destinations",
            Enabled = true,
            CreatedUtc = DateTime.UtcNow,
            ModifiedUtc = DateTime.UtcNow,
            Ingress = new Ingress
            {
                ApiKeyHash = new ApiKeyHash(new ApiKey(apiKey).ToHash()),
                Enabled = true,
            },
            Delivery = new Delivery
            {
                Authentication = new OutboundAuthentication(),
                Enabled = true,
                Headers = new HeaderCollection(),
                Destination = new Destination { Uri = new Uri($"http://httpbin.org/status/{destHttpReturnCode}") },
                Settings = new DeliverySettings
                {
                    RequestsPerInterval = 5,
                    DeliveryIntervalMillis = 1000,
                    InitialRetryDelayMillis = 1000,
                    MaximumRetryDelayMillis = 32000,
                    MaxQueueCapacity = 10000,
                    MaxRetryAttempts = 5,
                    RequestTimeoutMillis = 2000,
                    RetryBackoffMultiplier = 2.0
                },
            }
        };
    }
}
