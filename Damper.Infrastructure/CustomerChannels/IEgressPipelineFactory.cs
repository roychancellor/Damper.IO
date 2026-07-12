using Damper.Infrastructure.Repositories;

namespace Damper.Infrastructure.CustomerChannels
{
    public interface IEgressPipelineFactory
    {
        CustomerEgressPipeline CreatePipeline(CustomerConfig customerConfig, CancellationToken ct);
    }
}