using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Options;

namespace Damper.Infrastructure.Extensions
{
    public static class MessagingBrokerExtensions
    {
        public static IServiceCollection AddRabbitMqInfrastructure(this IServiceCollection services)
        {
            // Rabbit MQ wants its connection to be LONG-LIVED, so register as a singleton
            services.AddSingleton<IConnection>(serviceProvider =>
            {
                // This lambda runs LAZILY when a service first requests IConnection.
                // The container is fully built here, so we can safely resolve services.
                var appOptMon = serviceProvider.GetRequiredService<IOptionsMonitor<AppRefData>>();
                var rabbitRefData = appOptMon.CurrentValue.RabbitMqData;

                var connectionFactory = new ConnectionFactory 
                { 
                    HostName = rabbitRefData.HostName,
                    Port = rabbitRefData.Port,
                    UserName = rabbitRefData.UserName,
                    Password = rabbitRefData.Password, // Automatically bound from environment variable
                    VirtualHost = rabbitRefData.VirtualHost ?? "/"
                };

                // Because this lambda is synchronous, block once during 
                // initialization to instantiate the long-lived TCP socket.
                return connectionFactory.CreateConnectionAsync().GetAwaiter().GetResult();
            });

            return services;
        }
    }
}