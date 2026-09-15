using Damper.Application.Integrations;

namespace Damper.API.Startup
{
    public class Startup
    {
        public static async Task SeedIntegrationsIfEmpty(WebApplication app)
        {
            using var scope = app.Services.CreateScope();

            var integrationService = scope.ServiceProvider.GetRequiredService<IIntegrationService>();

            if ((await integrationService.GetAllAsync()).Count == 0)
            {
                await IntegrationDatabaseSeeder.SeedDevelopmentIntegrationsAsync(integrationService);
            }
        }
    }
}
