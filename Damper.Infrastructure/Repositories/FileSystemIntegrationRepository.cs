namespace Damper.Infrastructure.Repositories;

public class FileSystemIntegrationRepository : IIntegrationRepository
{
    private static readonly List<CustomerConfig> _customers =
    [
        new CustomerConfig { CustomerId = "HTTP200", DestinationURL = "http://httpbin.org/status/200", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP202", DestinationURL = "http://httpbin.org/status/202", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP301", DestinationURL = "http://httpbin.org/status/301", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP400", DestinationURL = "http://httpbin.org/status/400", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP401", DestinationURL = "http://httpbin.org/status/401", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP403", DestinationURL = "http://httpbin.org/status/403", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP429", DestinationURL = "http://httpbin.org/status/429", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
        new CustomerConfig { CustomerId = "HTTP500", DestinationURL = "http://httpbin.org/status/500", DeliveryRate = 5, DeliveryIntervalMillis = 1000, },
    ];
    
    public async Task<CustomerConfig?> GetByIdAsync(string customerId, CancellationToken ct)
    {
        // TODO: Actually go get config from file system
        var toReturn = _customers.FirstOrDefault(c => c.CustomerId.Equals(customerId, StringComparison.OrdinalIgnoreCase));
        return await Task.FromResult(toReturn);
    }
}
