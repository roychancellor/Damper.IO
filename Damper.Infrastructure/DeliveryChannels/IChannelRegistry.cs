namespace Damper.Infrastructure.DeliveryChannels
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