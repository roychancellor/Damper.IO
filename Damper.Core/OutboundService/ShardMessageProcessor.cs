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
        private static readonly ILogger _traceLog = Loggers.RequestTrace;
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

                _traceLog.Trace($"====> ProcessMessageAsync: Processing binary message | SHARD INDEX: {context.ShardIndex}");
                var amqpHeaders = ea.BasicProperties.Headers;
                if (amqpHeaders == null)
                {
                    _log.Error($"Consumed message is missing AMQP headers - Rejecting to DLQ. | SHARD INDEX: {context.ShardIndex}");
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
                
                // Allow NLog to automatically populate the Correlation Id in every log statement in this method beyond this point
                using var correlationScope = _log.BeginCorrelationScope(correlationId);
                
                int attemptCount = amqpHeaders.TryGetValue("x-damper-attempt-count", out var attemptObj)
                    ? Convert.ToInt32(attemptObj)
                    : 1;

                _traceLog.Trace($"Retrieved message headers | CUST ID: {customerId} | DEST URL: {destinationUrl} | CORR ID: {correlationId} | ATTEMPT: {attemptCount}");

                var envelopeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                string headerValueString = string.Empty;
                foreach (var (key, value) in amqpHeaders)
                {
                    if (key.StartsWith("h_") && value is byte[] headerValueBytes)
                    {
                        headerValueString = Encoding.UTF8.GetString(headerValueBytes);
                        envelopeHeaders[key[2..]] = headerValueString;
                        _traceLog.Trace($"Envelope header: KEY: {key} | VALUE: {headerValueString}");
                    }
                }

                _traceLog.Trace($"Building new WebhookEnvelope and settings is AckContext property (retrieved from pool)");
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

                _traceLog.Trace($"Getting customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                var pipeline = await _channelRegistry.GetOrCreatePipelineAsync(envelope.CustomerId);
                
                // SELF-HEALING: If infrastructure is dead, reset registry and NACK for retry
                if (pipeline.BackgroundTask.IsCompleted)
                {
                    _log.Warn("Retrieved pipeline for customer is dead - Resetting registry and NACKing for retry. | CUST ID: {Id}", envelope.CustomerId);
                    _channelRegistry.ResetPipeline(envelope.CustomerId);
                    await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    _contextPool.Return(ackContext);
                    return;
                }

                _traceLog.Trace($"Attempting non-blocking write to customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {ea.DeliveryTag}");
                
                // TODO: Get parking lot stay duration from appsettings
                var parkedAt = DateTime.UtcNow;
                var maxParkingDuration = TimeSpan.FromMinutes(5);

                while (_channelRegistry.IsSuspended(envelope.CustomerId))
                {
                    _traceLog.Trace($"Customer is suspended and waiting in parking lot | CUST ID: {envelope.CustomerId}");
                    if (StayLimitExceeded(parkedAt, maxParkingDuration))
                    {
                        _log.Warn("Parking lot stay limit exceeded for customer - Moving to DLQ. | CUST ID: {Id}", envelope.CustomerId);
                        await context.RejectAsync(ea.DeliveryTag, requeue: false);
                        DamperMetrics.DeadLetterCounter.Add(1, 
                            new KeyValuePair<string, object?>("customer_id", envelope.CustomerId),
                            new KeyValuePair<string, object?>("reason", "parking-limit-exceeded"));
                        _contextPool.Return(ackContext);
                        return;
                    }
                    // TODO: Get parking lot while loop delay from appsettings
                    await Task.Delay(TimeSpan.FromSeconds(30), context.StoppingToken);
                }

                _traceLog.Trace($"Attempting to write message to channel | CUST ID: {envelope.CustomerId}");
                
                // TODO: Get the wait to write token expiration time from appsettings
                var canWrite = await pipeline.Writer.WaitToWriteAsync(new CancellationTokenSource(1000).Token);
                if (canWrite && !pipeline.BackgroundTask.IsCompleted && pipeline.Writer.TryWrite(envelope))
                {
                    // DO NOT ACK HERE!!! LET THE CHANNEL DISPATCHER HANDLE THE ACK WHEN IT KNOWS THE OUTCOME
                    _traceLog.Info($"<==== ProcessMessageAsync: Successfully enqueued envelope in channel | CUST ID: {envelope.CustomerId} | DELIVERY TAG: {ea.DeliveryTag}");
                    _log.Info($"Successfully enqueued envelope in channel | CUST ID: {envelope.CustomerId} | DELIVERY TAG: {ea.DeliveryTag}");
                }
                else
                {
                    if (pipeline.BackgroundTask.IsCompleted)
                    {
                        _log.Warn("Pipeline crashed during wait for customer - Resetting registry and NACKing. | CUST ID: {Id}", envelope.CustomerId);
                        _channelRegistry.ResetPipeline(envelope.CustomerId);
                        await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    }
                    else
                    {
                        _log.Warn("Customer buffer is full or suspended - NACKing message to free up Shard | CUST ID: {Id} | SHARD: {Idx}.", envelope.CustomerId, context.ShardIndex);
                        await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
                    }
                    _contextPool.Return(ackContext);
                }
            }
            catch (ArgumentNullException aex)
            {
                if (ackContext != null) _contextPool.Return(ackContext);
                _log.Error($"While attempting to process message - argument is null - unable to proceed. | ERR MSG: {aex.Message}");
                throw;
            }
            catch (ChannelClosedException)
            {
                _log.Warn("Attempted to write to a closed channel for customer - resuming customer | CUST ID: {Id}.", envelope.CustomerId);
                _channelRegistry.ResumeCustomer(envelope.CustomerId);
                throw;
            }
            catch (OperationCanceledException)
            {
                _log.Warn($"ShardMessageProcessor: Shutdown requested | SHARD: {context.ShardIndex}");
                return;
            }
            catch (Exception ex)
            {
                if (ackContext != null) { _contextPool.Return(ackContext); }
                _log.Error(ex, "Fatal error on shard parsing layer - NACKing message. | SHARD: {Idx}", context.ShardIndex);
                await context.NackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        }

        private static bool StayLimitExceeded(DateTime parkedAt, TimeSpan maxParkingDuration)
        {
            return DateTime.UtcNow - parkedAt > maxParkingDuration;
        }
    }
}