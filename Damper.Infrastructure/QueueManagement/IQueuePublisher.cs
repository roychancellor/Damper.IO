namespace Damper.Infrastructure.QueueManagement
{
    public interface IQueuePublisher
    {
        Task<bool> PublishAsync(string correlationId, string customerId, string toPublish, CancellationToken ct, bool shouldThrow);
    }
}