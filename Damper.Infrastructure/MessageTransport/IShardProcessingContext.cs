namespace Damper.Infrastructure.MessageTransport
{
    public interface IShardProcessingContext
    {
        int ShardIndex { get; }
        CancellationToken StoppingToken { get; }
        Task AckAsync(ulong deliveryTag);
        Task RejectAsync(ulong deliveryTag, bool requeue);
        Task NackAsync(ulong deliveryTag, bool multiple, bool requeue);
        Task ParkForRetryAsync(MessageEnvelope envelope, ulong deliveryTag);
    }
}