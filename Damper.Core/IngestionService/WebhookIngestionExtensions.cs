using Microsoft.Extensions.DependencyInjection;

namespace Damper.Core.IngestionService
{
    public static class WebhookIngestionExtensions
    {
        public static IServiceCollection AddWebhookIngestion(this IServiceCollection services)
        {
            services.AddScoped<IWebhookIngestionService, WebhookIngestionService>();
            return services;
        }
    }
}