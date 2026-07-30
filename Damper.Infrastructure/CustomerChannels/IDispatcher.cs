using Damper.Infrastructure.Models;
using Damper.Infrastructure.Repositories;

namespace Damper.Infrastructure.CustomerChannels
{
    public interface IDispatcher
    {
        Task RunLoopAsync(CancellationToken ct);
        Task RefreshConfigAsync(CancellationToken ct);
        Task<bool> DeliverWebhookWithRetryAsync(WebhookEnvelope envelope, CustomerConfig config, CancellationToken ct);
    }
}