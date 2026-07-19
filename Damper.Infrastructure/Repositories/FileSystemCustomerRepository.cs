namespace Damper.Infrastructure.Repositories;

public class FileSystemCustomerRepository : ICustomerRepository
{
    public async Task<CustomerConfig?> GetByIdAsync(string customerId, CancellationToken ct)
    {
        // TODO: Actually go get config from file system
        return await Task.FromResult(new CustomerConfig
        {
            CustomerId = "ABC123_400",
            WebhookHeaderKey = "X-Webhook-Header",
            DestinationURL = "http://httpbin.org/status/400",
            DeliveryRate = 5,
            DeliveryIntervalMillis = 1000,
        });
    }
}
