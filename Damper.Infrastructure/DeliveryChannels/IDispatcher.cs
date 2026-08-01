using Damper.Infrastructure.MessageTransport;
using Damper.Infrastructure.Repositories;

namespace Damper.Infrastructure.CustomerChannels
{
    public interface IDispatcher
    {
        Task RunLoopAsync(CancellationToken ct);
        Task RefreshConfigAsync(CancellationToken ct);
        Task<bool> DeliverWebhookWithRetryAsync(MessageEnvelope envelope, CustomerConfig config, CancellationToken ct);
    }
}