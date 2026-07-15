using Damper.Infrastructure.Repositories;

namespace Damper.Infrastructure.CustomerChannels
{
    public interface IEgressPipelineFactory
    {
        CustomerEgressPipeline CreatePipeline(CustomerConfig customerConfig, Action<string> onSuspensionTriggered, CancellationToken ct);
    }
}