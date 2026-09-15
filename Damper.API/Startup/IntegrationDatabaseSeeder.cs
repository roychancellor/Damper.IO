using Damper.Application.Integrations;
using Damper.Domain.Common;
using Damper.Domain.Integrations;
using Damper.Domain.Integrations.OutAuthentication;
using Damper.Infrastructure.Security;

namespace Damper.API.Startup
{
    public class IntegrationDatabaseSeeder
    {
        public static async Task SeedDevelopmentIntegrationsAsync(IIntegrationService integrationService)
        {
            var responseCodes = new[]
            {
                200, 202, 301, 400, 401, 403, 429, 500
            };

            foreach (var responseCode in responseCodes)
            {
                var apiKey = $"HTTP{responseCode}";

                var integration = new Integration
                {
                    Name = new($"{apiKey} Integration"),
                    Description = $"Testing {apiKey} destinations",
                    Enabled = true,

                    Ingress = new Ingress
                    {
                        ApiKeyHash = new ApiKey(apiKey).ToHash(),
                        Enabled = true
                    },

                    Delivery = new Delivery
                    {
                        Authentication = new NoAuthentication(),
                        Enabled = true,
                        Headers = new HeaderCollection(),
                        Destination = new Destination
                        {
                            Uri = new Uri($"https://httpbin.org/status/{responseCode}")
                        },
                        Settings = new DeliverySettings
                        {
                            RequestsPerInterval = 5,
                            DeliveryIntervalMillis = 1000,
                            InitialRetryDelayMillis = 1000,
                            MaximumRetryDelayMillis = 32000,
                            MaxQueueCapacity = 10000,
                            MaxRetryAttempts = 5,
                            RequestTimeoutMillis = 2000,
                            RetryBackoffMultiplier = 2.0
                        }
                    }
                };

                await integrationService.SaveAsync(integration);
            }
        }
    }
}
