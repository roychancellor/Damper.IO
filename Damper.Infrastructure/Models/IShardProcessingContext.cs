using RabbitMQ.Client.Events;

namespace Damper.Infrastructure.Models
{
    public interface IShardProcessingContext
    {
        int ShardIndex { get; }
        Task AckAsync(ulong deliveryTag);
        Task RejectAsync(ulong deliveryTag, bool requeue);
        Task NackAsync(ulong deliveryTag, bool multiple, bool requeue);
    }
}