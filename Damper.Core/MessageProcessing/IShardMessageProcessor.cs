using Damper.Infrastructure.MessageTransport;
using RabbitMQ.Client.Events;

namespace Damper.Core.MessageProcessing
{
    public interface IShardMessageProcessor
    {
        Task ProcessMessageAsync(BasicDeliverEventArgs ea, IShardProcessingContext context);
    }
}