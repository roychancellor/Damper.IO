using Damper.Application.Integrations;
using Damper.Infrastructure.Persistence;
using Damper.Infrastructure.Persistence.PostgreSql;
using Damper.Infrastructure.ReferenceData;
using Dapper;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Damper.Infrastructure.Extensions
{
    public static class RepositoryExtensions
    {
        public static IServiceCollection AddRepositories(this IServiceCollection services)
        {
            DefaultTypeMap.MatchNamesWithUnderscores = true;

            services.AddMemoryCache();
            services.AddScoped<PostgreSqlIntegrationRepository>();
            services.AddScoped<FileSystemIntegrationRepository>();

            // Register the decorator to intercept calls
            services.AddScoped<IIntegrationRepository>(provider => 
                new CachedIntegrationRepository(
                    provider.GetRequiredService<PostgreSqlIntegrationRepository>(),
                    //provider.GetRequiredService<FileSystemIntegrationRepository>(),
                    provider.GetRequiredService<IMemoryCache>(),
                    provider.GetRequiredService<IOptionsMonitor<AppSettings>>()
                ));
            return services;
        }
    }
}