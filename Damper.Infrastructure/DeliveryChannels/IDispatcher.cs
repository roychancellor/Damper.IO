using Damper.Domain.Integrations;
using Damper.Infrastructure.MessageTransport;

namespace Damper.Infrastructure.DeliveryChannels
{
    public interface IDispatcher
    {
        Task RunLoopAsync(CancellationToken ct);
        Task RefreshConfigAsync(CancellationToken ct);
        Task<bool> DeliverMessageWithRetryAsync(MessageEnvelope envelope, Integration integration, CancellationToken ct);
    }
}