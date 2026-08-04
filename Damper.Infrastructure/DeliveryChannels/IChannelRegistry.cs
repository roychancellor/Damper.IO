using Damper.Infrastructure.CustomerChannels;

namespace Damper.Infrastructure.ChannelRegistry
{
    public interface IChannelRegistry
    {
        Task<EgressPipeline> GetOrCreatePipelineAsync(long integrationId);
        void MarkAsSuspended(long integrationId);
        Task AutoResumeAfterCooldownAsync(long integrationId, TimeSpan cooldown);
        void ResumeIntegration(long integrationId);
        bool IsSuspended(long integrationId);
        void ResetPipeline(long integrationId);
    }
}