using Damper.Application.Integrations;
using Damper.Domain.Common;
using Damper.Domain.Integrations;

namespace Damper.Infrastructure.Persistence.PostgreSql
{
    // TODO: Implement the Postgres repository once Postgres is installed and running
    public class PostgreSqlIntegrationRepository : IIntegrationRepository
    {
        public Task DeleteAsync(long integrationId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyCollection<Integration>> GetAllAsync(CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Integration?> GetByApiKeyHashAsync(ApiKeyHash apiKeyHash, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task<Integration?> GetByIdAsync(long integrationId, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        public Task SaveAsync(Integration integration, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }
    }
}