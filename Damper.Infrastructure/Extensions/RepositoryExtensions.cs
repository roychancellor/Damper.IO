using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.Caching.Memory;
namespace Damper.Infrastructure.Extensions
{
    public static class RepositoryExtensions
    {
        public static IServiceCollection AddRepositories(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddMemoryCache();
            services.AddScoped<PostgreSqlCustomerRepository>();
            services.AddScoped<FileSystemCustomerRepository>();

            // Register the decorator to intercept calls
            services.AddScoped<ICustomerRepository>(provider => 
                new CachedCustomerRepository(
                    //provider.GetRequiredService<PostgreSqlCustomerRepository>(),
                    provider.GetRequiredService<FileSystemCustomerRepository>(),
                    provider.GetRequiredService<IMemoryCache>()
                ));
            return services;
        }
    }
}