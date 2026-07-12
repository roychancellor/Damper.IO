using System.Text;
using System.Text.Json;
using Damper.Infrastructure.ChannelRegistry;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client.Events;

namespace Damper.Core.OutboundService
{
    public class ShardMessageProcessor : IShardMessageProcessor
    {
        private static readonly ILogger _log = Loggers.Request;

        private readonly IChannelRegistry _channelRegistry;

        public ShardMessageProcessor(IChannelRegistry channelRegistry)
        {
            _channelRegistry = channelRegistry;
        }

        public async Task ProcessMessageAsync(BasicDeliverEventArgs ea, IShardProcessingContext context)
        {
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

                envelope.DeliveryTag = ea.DeliveryTag;
                
                // Reconstruct the thread-safe durable callback bridge via context wrapper
                envelope.OnProcessingCompleteAsync = async () =>
                {
                    try
                    {
                        await context.AckAsync(ea.DeliveryTag);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to ACK delivery tag {Tag} on Shard {Idx}", ea.DeliveryTag, context.ShardIndex);
                    }
                };

                _log.Debug($"Getting customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                var writer = await _channelRegistry.GetOrCreateChannel(envelope.CustomerId);
                
                _log.Debug($"Attempting non-blocking write to customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                
                // Use TryWrite instead of an awaited WriteAsync to eliminate Head-of-Line blocking
                if (!writer.TryWrite(envelope))
                {
                    _log.LogWarning("Customer {Id} buffer is full or suspended. NACKing message to free up Shard {Idx}.", envelope.CustomerId, context.ShardIndex);

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
                _log.Error($"While attempting to process message - argument is null - unable to proceed. | MSG: {aex.Message}");
                throw;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Fatal error on shard parsing layer {Idx}. NACKing message.", context.ShardIndex);
                await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        }
    }
}