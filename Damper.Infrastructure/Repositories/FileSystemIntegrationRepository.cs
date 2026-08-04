using Damper.Domain.Common;
using Damper.Domain.Integrations;

namespace Damper.Infrastructure.Repositories;

public class FileSystemIntegrationRepository : IIntegrationRepository
{
    private static readonly List<Integration> _integrations =
    [
        new Integration { Id = 1,
                          Name = "HTTP200 Integration",
                          Description = "Testing HTTP200 destinations", Enabled = true, Version = 1, CreatedUtc = DateTime.UtcNow, ModifiedUtc = DateTime.UtcNow,
                          Ingress = new IngressEndpoint
                          {
                            Id = 1, Authentication = new IngressAuthentication { ApiKey = new("HTTP200") },
                            Enabled = true, Name = "HTTP200 Endpoint", MessageRouteId = 1
                          },
                          Route = new MessageRoute
                          {
                            Id = 1,
                            Authentication = new OutboundAuthentication(),
                            Delivery = new DeliverySettings { DeliveryIntervalMillis = 1000, InitialRetryDelayMillis = 1000, MaximumRetryDelayMillis = 32000,
                                                              MaxQueueCapacity = 10000, MaxRetryAttempts = 5, RequestsPerInterval = 10, RequestTimeoutMillis = 2000,
                                                              RetryBackoffMultiplier = 2.0 },
                            Enabled = true,
                            Headers = new HeaderCollection(),
                            Name = "HTTP200 Route",
                            Target = new MessageDestination { Uri = new Uri("http://httpbin.org/status/200") }
                          }
                        },
        /*
        new CustomerConfig { CustomerId = "HTTP200", DestinationURL = "http://httpbin.org/status/200", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP202", DestinationURL = "http://httpbin.org/status/202", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP301", DestinationURL = "http://httpbin.org/status/301", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP400", DestinationURL = "http://httpbin.org/status/400", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP401", DestinationURL = "http://httpbin.org/status/401", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP403", DestinationURL = "http://httpbin.org/status/403", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP429", DestinationURL = "http://httpbin.org/status/429", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP500", DestinationURL = "http://httpbin.org/status/500", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        */
    ];

    public async Task DeleteAsync(long integrationId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<IReadOnlyCollection<Integration>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public async Task<Integration?> GetByApiKeyAsync(ApiKey apiKey, CancellationToken cancellationToken = default)
    {
        // TODO: Actually go get config from file system
        var toReturn = _integrations.FirstOrDefault(i => i.Ingress.Authentication.ApiKey.Value.Equals(apiKey.Value, StringComparison.OrdinalIgnoreCase));
        return await Task.FromResult(toReturn);
    }

    public async Task<Integration?> GetByIdAsync(long integrationId, CancellationToken cancellationToken = default)
    {
        // TODO: Actually go get config from file system
        var toReturn = _integrations.FirstOrDefault(i => i.Id == integrationId);
        return await Task.FromResult(toReturn);
    }

    public async Task SaveAsync(Integration integration, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}
