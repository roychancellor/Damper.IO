namespace Damper.Infrastructure.QueueManagement
{
    public interface IQueuePublisher
    {
        Task<bool> PublishAsync(string customerId, string toPublish, CancellationToken ct);
    }
}