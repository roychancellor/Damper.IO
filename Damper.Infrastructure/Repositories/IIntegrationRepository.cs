using Damper.Domain.Common;
using Damper.Domain.Integrations;

namespace Damper.Infrastructure.Repositories
{
    public interface IIntegrationRepository
    {
        Task<Integration?> GetByIdAsync(long integrationId, CancellationToken cancellationToken = default);

        Task<Integration?> GetByApiKeyAsync(ApiKey apiKey, CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<Integration>> GetAllAsync(CancellationToken cancellationToken = default);

        Task SaveAsync(Integration integration, CancellationToken cancellationToken = default);

        Task DeleteAsync(long integrationId, CancellationToken cancellationToken = default);
    }
}