using Damper.Domain.Common;
using Damper.Domain.Integrations;

namespace Damper.Application.Integrations
{
    public sealed class IntegrationService : IIntegrationService
    {
        private readonly IIntegrationRepository _repository;

        public IntegrationService(IIntegrationRepository repository)
        {
            _repository = repository;
        }

        public Task<Integration?> GetByIdAsync(long integrationId, CancellationToken cancellationToken = default)
            => _repository.GetByIdAsync(integrationId, cancellationToken);

        public Task<Integration?> GetByApiKeyHashAsync(ApiKeyHash apiKeyHash, CancellationToken cancellationToken = default)
            => _repository.GetByApiKeyHashAsync(apiKeyHash, cancellationToken);

        public Task<IReadOnlyCollection<Integration>> GetAllAsync(CancellationToken cancellationToken = default)
            => _repository.GetAllAsync(cancellationToken);

        public Task<Integration> SaveAsync(Integration integration, CancellationToken cancellationToken = default)
            => _repository.SaveAsync(integration, cancellationToken);

        public Task DeleteAsync(long integrationId, CancellationToken cancellationToken = default)
            => _repository.DeleteAsync(integrationId, cancellationToken);
    }
}