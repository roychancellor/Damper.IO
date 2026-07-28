using System.Threading.Channels;
using Damper.Infrastructure.CustomerChannels;
using Damper.Infrastructure.Models;

namespace Damper.Infrastructure.ChannelRegistry
{
    public interface IChannelRegistry
    {
        Task<CustomerEgressPipeline> GetOrCreatePipelineAsync(string customerId);
        void MarkAsSuspended(string customerId);
        Task AutoResumeAfterCooldownAsync(string customerId, TimeSpan cooldown);
        void ResumeCustomer(string customerId);
        bool IsSuspended(string customerId);
        void EvictPipeline(string customerId);
        void ResetPipeline(string customerId);
    }
}