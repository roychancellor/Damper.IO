using Damper.Domain.Integrations;
using Damper.Infrastructure.Repositories;

namespace Damper.Infrastructure.CustomerChannels
{
    public interface IEgressPipelineFactory
    {
        EgressPipeline CreatePipeline(Integration integration, Action<long> onSuspensionTriggered, CancellationToken ct);
    }
}