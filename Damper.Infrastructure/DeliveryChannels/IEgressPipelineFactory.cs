using Damper.Infrastructure.Repositories;

namespace Damper.Infrastructure.CustomerChannels
{
    public interface IEgressPipelineFactory
    {
        EgressPipeline CreatePipeline(CustomerConfig customerConfig, Action<string> onSuspensionTriggered, CancellationToken ct);
    }
}