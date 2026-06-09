using Microsoft.Extensions.DependencyInjection;
using Damper.Infrastructure.QueueManagement;

namespace Damper.Infrastructure.Extensions
{
    public static class QueuePublisherExtensions
    {
        public static IServiceCollection AddQueuePublishing(this IServiceCollection services)
        {
            services.AddScoped<IQueuePublisher, RabbitMQQueuePublisher>();
            
            return services;
        }
    }
}