namespace Damper.Infrastructure.QueueManagement
{
    public interface IQueuePublisher
    {
        Task<bool> TryPublishAsync(PublishWrapper publishWrapper);
    }
}