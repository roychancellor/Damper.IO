namespace Damper.Infrastructure.Repositories
{
    public interface IIntegrationRepository
    {
        Task<CustomerConfig?> GetByIdAsync(string customerId, CancellationToken ct);
    }
}