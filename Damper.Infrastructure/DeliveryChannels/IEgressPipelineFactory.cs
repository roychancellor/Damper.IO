using Damper.Domain.Integrations;

namespace Damper.Infrastructure.DeliveryChannels
{
    public interface IEgressPipelineFactory
    {
        EgressPipeline CreatePipeline(Integration integration, Action<long> onSuspensionTriggered, CancellationToken ct);
    }
}