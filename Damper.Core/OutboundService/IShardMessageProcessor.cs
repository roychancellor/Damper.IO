using RabbitMQ.Client.Events;

namespace Damper.Core.OutboundService
{
    public interface IShardMessageProcessor
    {
        Task ProcessMessageAsync(BasicDeliverEventArgs ea, IShardProcessingContext context);
    }
}