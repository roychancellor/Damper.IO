namespace Damper.Infrastructure.Repositories;

public class FileSystemCustomerRepository : ICustomerRepository
{
    public async Task<CustomerConfig?> GetByIdAsync(string customerId, CancellationToken ct)
    {
        // TODO: Actually go get config from file system
        return await Task.FromResult(new CustomerConfig
        {
            CustomerId = "ABC123",
            SecretKey = "123-456-789",
            WebhookHeaderKey = "X-Webhook-Header",
            DestinationURL = "https://customer.endpoint/webhook/post",
            DeliveryRate = 5,
            DeliveryIntervalMillis = 1000,
        });
    }
}
