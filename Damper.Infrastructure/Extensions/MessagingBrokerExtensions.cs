using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using RabbitMQ.Client;

namespace Damper.Infrastructure.Extensions
{
    public static class MessagingBrokerExtensions
    {
        public static async Task<IServiceCollection> AddRabbitMqInfrastructureAsync(this IServiceCollection services, IConfiguration configuration)
        {
            // Pull host from appsettings.json instead of hardcoding
            // TODO: MessagingBrokerExtensions: Get this from an IOptionsMonitor injection
            string hostName = configuration["RabbitMQ:HostName"] ?? "localhost";

            var connectionFactory = new ConnectionFactory 
            { 
                HostName = hostName 
            };

            // 1. Initialize and register the single, long-lived TCP connection
            IConnection connection = await connectionFactory.CreateConnectionAsync();
            services.AddSingleton(connection);

            // 2. Register the thread-isolated, short-lived channel factory per request
            services.AddScoped(async provider =>
            {
                var conn = provider.GetRequiredService<IConnection>();
                return await conn.CreateChannelAsync();
            });

            return services;
        }
    }
}