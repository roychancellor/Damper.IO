namespace Damper.Infrastructure.Repositories
{
    public interface ICustomerRepository
    {
        Task<CustomerConfig?> GetByIdAsync(string customerId, CancellationToken ct);
    }
}