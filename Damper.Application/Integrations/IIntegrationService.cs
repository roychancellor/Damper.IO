using Damper.Domain.Common;
using Damper.Domain.Integrations;

namespace Damper.Application.Integrations
{
    public interface IIntegrationService
    {
        Task<Integration?> GetByIdAsync(long integrationId, CancellationToken cancellationToken = default);

        Task<Integration?> GetByApiKeyHashAsync(ApiKeyHash apiKeyHash, CancellationToken cancellationToken = default);

        Task<IReadOnlyCollection<Integration>> GetAllAsync(CancellationToken cancellationToken = default);

        Task<Integration> SaveAsync(Integration integration, CancellationToken cancellationToken = default);

        Task DeleteAsync(long integrationId, CancellationToken cancellationToken = default);
    }
}