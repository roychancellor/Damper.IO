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

        // TODO: DECOUPLE THE CREATING OF THE CHANNEL INTO A FACTORY - REFER TO GEMINI CHAT FOR DETAILS
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
                _log.Debug($"Sending envelope to customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                await writer.WriteAsync(envelope);
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