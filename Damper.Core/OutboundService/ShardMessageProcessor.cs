using System.Text;
using System.Text.Json;
using Damper.Infrastructure.ChannelRegistry;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client.Events;
namespace Damper.Core.OutboundService
{
    public class ShardMessageProcessor : IShardMessageProcessor
    {
        private static readonly ILogger _log = Loggers.Request;
        private readonly IChannelRegistry _channelRegistry;
        private readonly ObjectPool<WebhookAckContext> _contextPool;

        public ShardMessageProcessor(IChannelRegistry channelRegistry, ObjectPool<WebhookAckContext> contextPool)
        {
            _channelRegistry = channelRegistry;
            _contextPool = contextPool;
        }

        public async Task ProcessMessageAsync(BasicDeliverEventArgs ea, IShardProcessingContext context)
        {
            WebhookAckContext? ackContext = null;
            try
            {
                ArgumentNullException.ThrowIfNull(context, nameof(context));

                _log.Debug($"Processing message | SHARD INDEX: {context.ShardIndex}");
                var bodyBytes = ea.Body.ToArray();
                var jsonString = Encoding.UTF8.GetString(bodyBytes);
                var envelope = JsonSerializer.Deserialize<WebhookEnvelope>(jsonString);

                if (envelope is null)
                {
                    _log.Error($"After deserializing the WebhookEnvelope payload, envelope is null. Rejecting.");
                    _log.Debug($"Payload: {jsonString}");
                    await context.RejectAsync(ea.DeliveryTag, requeue: false);
                    return;
                }

                // Rent an execution context from the pool instead of instantiating an anonymous lambda closure
                ackContext = _contextPool.Get();
                ackContext.DeliveryTag = ea.DeliveryTag;
                ackContext.ShardIndex = context.ShardIndex;
                ackContext.ShardContext = context;

                envelope.AckContext = ackContext;

                _log.Debug($"Getting customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                var writer = await _channelRegistry.GetOrCreateChannel(envelope.CustomerId);
                
                _log.Debug($"Attempting non-blocking write to customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                
                // Use TryWrite instead of an awaited WriteAsync to eliminate Head-of-Line blocking
                if (!writer.TryWrite(envelope))
                {
                    _log.LogWarning("Customer {Id} buffer is full or suspended. NACKing message to free up Shard {Idx}.", envelope.CustomerId, context.ShardIndex);

                    // Return the context to the pool immediately since the message is going back to the broker
                    _contextPool.Return(ackContext);

                    // Return the message to the broker so other customers' traffic can pass through
                    await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);

                    // Small 50ms pause prevents this specific shard thread from hammering RabbitMQ 
                    // in a tight loop if the queue contains only this blocked customer's data.
                    await Task.Delay(50);
                    return;
                }
                
                _log.Debug($"Successfully enqueued envelope | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
            }
            catch (ArgumentNullException aex)
            {
                if (ackContext != null)
                {
                    _contextPool.Return(ackContext);
                }
                _log.Error($"While attempting to process message - argument is null - unable to proceed. | MSG: {aex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                if (ackContext != null)
                {
                    _contextPool.Return(ackContext);
                }
                _log.Error(ex, "Fatal error on shard parsing layer {Idx}. NACKing message.", context.ShardIndex);
                await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        }
    }
}