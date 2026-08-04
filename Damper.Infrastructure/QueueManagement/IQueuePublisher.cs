using Damper.Infrastructure.MessageTransport;

namespace Damper.Infrastructure.QueueManagement
{
    public interface IQueuePublisher
    {
        Task<bool> TryPublishAsync(MessageEnvelope msgEnv);
    }
}