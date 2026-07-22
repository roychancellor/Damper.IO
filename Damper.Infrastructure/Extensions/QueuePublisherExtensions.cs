using Microsoft.Extensions.DependencyInjection;
using Damper.Infrastructure.QueueManagement;

namespace Damper.Infrastructure.Extensions
{
    public static class QueuePublisherExtensions
    {
        public static IServiceCollection AddQueuePublishing(this IServiceCollection services)
        {
            services.AddSingleton<IQueuePublisher, RabbitMQQueuePublisher>();
            
            return services;
        }
    }
}