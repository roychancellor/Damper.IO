using System.Text;
using System.Threading.Channels;
using Damper.Infrastructure.ChannelRegistry;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Observability;
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
            WebhookEnvelope envelope = new();
            try
            {
                ArgumentNullException.ThrowIfNull(context, nameof(context));

                _log.Debug($"Processing binary message | SHARD INDEX: {context.ShardIndex}");
                var amqpHeaders = ea.BasicProperties.Headers;
                if (amqpHeaders == null)
                {
                    _log.Error("Message is missing AMQP headers. Rejecting.");
                    await context.RejectAsync(ea.DeliveryTag, requeue: false);
                    return;
                }

                string GetStringHeader(string key)
                {
                    return amqpHeaders.TryGetValue(key, out var val) && val is byte[] bytes
                        ? Encoding.UTF8.GetString(bytes)
                        : string.Empty;
                }

                var customerId = GetStringHeader("x-damper-customer-id");
                var destinationUrl = GetStringHeader("x-damper-destination-url");
                var correlationId = GetStringHeader("x-damper-correlation-id");
                
                int attemptCount = amqpHeaders.TryGetValue("x-damper-attempt-count", out var attemptObj)
                    ? Convert.ToInt32(attemptObj)
                    : 1;

                var envelopeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var (key, value) in amqpHeaders)
                {
                    if (key.StartsWith("h_") && value is byte[] headerValueBytes)
                    {
                        envelopeHeaders[key[2..]] = Encoding.UTF8.GetString(headerValueBytes);
                    }
                }

                envelope = new WebhookEnvelope
                {
                    CorrelationId = correlationId,
                    CustomerId = customerId,
                    DestinationUrl = destinationUrl,
                    Headers = envelopeHeaders,
                    AttemptCount = attemptCount,
                    ReceivedAt = DateTime.UtcNow,
                    RawPayloadBytes = ea.Body 
                };

                ackContext = _contextPool.Get();
                ackContext.DeliveryTag = ea.DeliveryTag;
                ackContext.ShardIndex = context.ShardIndex;
                ackContext.ShardContext = context;

                envelope.AckContext = ackContext;

                _log.Debug($"Getting customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                var pipeline = await _channelRegistry.GetOrCreatePipelineAsync(envelope.CustomerId);
                
                // SELF-HEALING: If infrastructure is dead, reset registry and NACK for retry
                if (pipeline.BackgroundTask.IsCompleted)
                {
                    _log.Warn("Pipeline is dead for {Id}. Resetting registry and NACKing for retry.", envelope.CustomerId);
                    _channelRegistry.ResetPipeline(envelope.CustomerId);
                    await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    _contextPool.Return(ackContext);
                    return;
                }

                _log.Debug($"Attempting non-blocking write to customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                
                var parkedAt = DateTime.UtcNow;
                var maxParkingDuration = TimeSpan.FromMinutes(5);

                while (_channelRegistry.IsSuspended(envelope.CustomerId))
                {
                    if (StayLimitExceeded(parkedAt, maxParkingDuration))
                    {
                        _log.Warn("Parking limit exceeded for {Id}. Moving to DLQ.", envelope.CustomerId);
                        await context.MoveToDeadLetterAsync(envelope);
                        DamperMetrics.DeadLetterCounter.Add(1, 
                            new KeyValuePair<string, object?>("customer_id", envelope.CustomerId),
                            new KeyValuePair<string, object?>("reason", "parking-limit-exceeded"));
                        await context.AckAsync(ea.DeliveryTag);
                        _contextPool.Return(ackContext);
                        return;
                    }
                    await Task.Delay(TimeSpan.FromSeconds(30), context.StoppingToken);
                }

                var canWrite = await pipeline.Writer.WaitToWriteAsync(new CancellationTokenSource(1000).Token);

                if (canWrite && !pipeline.BackgroundTask.IsCompleted && pipeline.Writer.TryWrite(envelope))
                {
                    // DO NOT ACK HERE!!! LET THE CHANNEL DISPATCHER HANDLE THE ACK WHEN IT KNOW THE OUTCOME
                    _log.Info($"Successfully enqueued envelope | CUST ID: {envelope.CustomerId} | DELIVERY TAG: {ea.DeliveryTag}");
                }
                else
                {
                    if (pipeline.BackgroundTask.IsCompleted)
                    {
                        _log.Warn("Pipeline crashed during wait for {Id}. Resetting registry and NACKing.", envelope.CustomerId);
                        _channelRegistry.ResetPipeline(envelope.CustomerId);
                        await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    }
                    else
                    {
                        _log.Warn("Customer {Id} buffer is full or suspended. NACKing message to free up Shard {Idx}.", envelope.CustomerId, context.ShardIndex);
                        await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    }
                    _contextPool.Return(ackContext);
                }
            }
            catch (ArgumentNullException aex)
            {
                if (ackContext != null) _contextPool.Return(ackContext);
                _log.Error($"While attempting to process message - argument is null - unable to proceed. | MSG: {aex.Message}");
                throw;
            }
            catch (ChannelClosedException)
            {
                _log.Warn("Attempted to write to a closed channel for {Id}.", envelope.CustomerId);
                _channelRegistry.ResumeCustomer(envelope.CustomerId);
                throw;
            }
            catch (OperationCanceledException)
            {
                _log.Warn($"ShardMessageProcessor: Shutdown requested");
                return;
            }
            catch (Exception ex)
            {
                if (ackContext != null) _contextPool.Return(ackContext);
                _log.Error(ex, "Fatal error on shard parsing layer {Idx}. NACKing message.", context.ShardIndex);
                await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        }

        private static bool StayLimitExceeded(DateTime parkedAt, TimeSpan maxParkingDuration)
        {
            return DateTime.UtcNow - parkedAt > maxParkingDuration;
        }
    }
}