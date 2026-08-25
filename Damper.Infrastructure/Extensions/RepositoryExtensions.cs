using Microsoft.Extensions.DependencyInjection;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using Damper.Infrastructure.ReferenceData;
using Damper.Application.Integrations;
using Damper.Infrastructure.Persistence.PostgreSql;

namespace Damper.Infrastructure.Extensions
{
    public static class RepositoryExtensions
    {
        public static IServiceCollection AddRepositories(this IServiceCollection services)
        {
            services.AddMemoryCache();
            services.AddScoped<PostgreSqlIntegrationRepository>();
            services.AddScoped<FileSystemIntegrationRepository>();

            // Register the decorator to intercept calls
            services.AddScoped<IIntegrationRepository>(provider => 
                new CachedIntegrationRepository(
                    //provider.GetRequiredService<PostgreSqlIntegrationRepository>(),
                    provider.GetRequiredService<FileSystemIntegrationRepository>(),
                    provider.GetRequiredService<IMemoryCache>(),
                    provider.GetRequiredService<IOptionsMonitor<AppSettings>>()
                ));
            return services;
        }
    }
}