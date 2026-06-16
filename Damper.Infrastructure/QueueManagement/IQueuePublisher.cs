namespace Damper.Infrastructure.QueueManagement
{
    public interface IQueuePublisher
    {
        Task<bool> PublishAsync(PublishWrapper publishWrapper);
    }
}