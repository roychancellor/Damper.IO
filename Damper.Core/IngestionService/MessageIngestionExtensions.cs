using Microsoft.Extensions.DependencyInjection;

namespace Damper.Core.IngestionService
{
    public static class MessageIngestionExtensions
    {
        public static IServiceCollection AddWebhookIngestion(this IServiceCollection services)
        {
            services.AddScoped<IMessageIngestionService, MessageIngestionService>();
            return services;
        }
    }
}