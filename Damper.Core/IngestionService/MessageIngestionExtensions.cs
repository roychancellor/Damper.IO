using Microsoft.Extensions.DependencyInjection;

namespace Damper.Core.IngestionService
{
    public static class MessageIngestionExtensions
    {
        public static IServiceCollection AddMessageIngestion(this IServiceCollection services)
        {
            services.AddScoped<IMessageIngestionService, MessageIngestionService>();
            return services;
        }
    }
}