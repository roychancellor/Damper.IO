using System.Text;
using System.Threading.Channels;
using Damper.Infrastructure.ChannelRegistry;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Observability;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Events;
namespace Damper.Core.OutboundService
{
    public class ShardMessageProcessor : IShardMessageProcessor
    {
        private static readonly ILogger _log = Loggers.Request;
        private static readonly ILogger _traceLog = Loggers.RequestTrace;
        private readonly IChannelRegistry _channelRegistry;
        private readonly ObjectPool<WebhookAckContext> _contextPool;
        private readonly IOptionsMonitor<AppSettings> _optMon;

        public ShardMessageProcessor(IChannelRegistry channelRegistry, ObjectPool<WebhookAckContext> contextPool, IOptionsMonitor<AppSettings> optMon)
        {
            _channelRegistry = channelRegistry;
            _contextPool = contextPool;
            _optMon = optMon;
        }

        public async Task ProcessMessageAsync(BasicDeliverEventArgs eventArgs, IShardProcessingContext context)
        {
            WebhookAckContext? ackContext = null;
            WebhookEnvelope envelope = new();
            try
            {
                ArgumentNullException.ThrowIfNull(context, nameof(context));

                _log.Info($"====> Processing new message | SHARD INDEX: {context.ShardIndex}");
                _traceLog.Trace($"====> ProcessMessageAsync: Processing binary message | SHARD INDEX: {context.ShardIndex}");
                var amqpHeaders = eventArgs.BasicProperties.Headers;
                if (amqpHeaders == null)
                {
                    _log.Error($"Consumed message is missing AMQP headers - Rejecting to DLQ. | SHARD INDEX: {context.ShardIndex}");
                    await context.RejectAsync(eventArgs.DeliveryTag, requeue: false);
                    return;
                }
                var customerId = amqpHeaders.GetStringHeader(DamperConstants.X_DAMPER_CUSTOMER_ID);
                var destinationUrl = amqpHeaders.GetStringHeader(DamperConstants.X_DAMPER_DESTINATION_URL);
                var correlationId = amqpHeaders.GetStringHeader(DamperConstants.X_DAMPER_CORRELATION_ID);

                // Allow NLog to automatically populate the Correlation Id in every log statement in this method beyond this point
                using var correlationScope = _log.BeginCorrelationScope(correlationId);

                int attemptCount = amqpHeaders.TryGetValue(DamperConstants.X_DAMPER_ATTEMPT_COUNT, out var attemptObj)
                                   ? Convert.ToInt32(attemptObj)
                                   : 1;

                _traceLog.Trace($"Retrieved message headers | CUST ID: {customerId} | DEST URL: {destinationUrl} | CORR ID: {correlationId} | ATTEMPT: {attemptCount}");
                var envelopeHeaders = GetEnvelopeHeaders(amqpHeaders);

                _traceLog.Trace($"Building new WebhookEnvelope and settings is AckContext property (retrieved from pool)");
                ackContext = _contextPool.Get();
                ackContext.DeliveryTag = eventArgs.DeliveryTag;
                ackContext.ShardIndex = context.ShardIndex;
                ackContext.ShardContext = context;

                envelope = new WebhookEnvelope
                {
                    CorrelationId = correlationId,
                    CustomerId = customerId,
                    DestinationUrl = destinationUrl,
                    Headers = envelopeHeaders,
                    AttemptCount = attemptCount,
                    ReceivedAt = DateTime.UtcNow,
                    RawPayloadBytes = eventArgs.Body,
                    AckContext = ackContext
                };

                _traceLog.Trace($"Getting customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {eventArgs.DeliveryTag}");
                var pipeline = await _channelRegistry.GetOrCreatePipelineAsync(envelope.CustomerId);

                _traceLog.Trace($"Attempting non-blocking write to customer channel | CUST ID: {envelope.CustomerId} | DEL TAG: {eventArgs.DeliveryTag}");

                var canWrite = await pipeline.Writer.WaitToWriteAsync(
                                new CancellationTokenSource(_optMon.CurrentValue.ProcessorSettings.WaitToWriteExpirationMillis).Token);
                if (canWrite && !pipeline.BackgroundTask.IsCompleted && pipeline.Writer.TryWrite(envelope))
                {
                    // DO NOT ACK HERE!!! LET THE CHANNEL DISPATCHER HANDLE THE ACK WHEN IT KNOWS THE OUTCOME
                    _traceLog.Trace($"<==== ProcessMessageAsync: Successfully enqueued envelope in channel | CUST ID: {envelope.CustomerId} | DELIVERY TAG: {eventArgs.DeliveryTag}");
                    _log.Info($"<==== Successfully enqueued envelope in channel | CUST ID: {envelope.CustomerId} | DELIVERY TAG: {eventArgs.DeliveryTag}");
                }
                else
                {
                    // Covers every reason we couldn't hand off: suspended (sentinel writer),
                    // buffer full, or a genuinely dead/crashed pipeline - all get parked for
                    // delayed automatic retry instead of an immediate, unthrottled NACK.
                    if (pipeline.BackgroundTask.IsCompleted && !_channelRegistry.IsSuspended(envelope.CustomerId))
                    {
                        _log.Warn("<==== Pipeline crashed for customer - Resetting registry. | CUST ID: {Id}", envelope.CustomerId);
                        _channelRegistry.ResetPipeline(envelope.CustomerId);
                    }
                    else
                    {
                        _log.Warn("<==== Customer buffer full or suspended - Parking message for delayed retry | CUST ID: {Id} | SHARD: {Idx}.", envelope.CustomerId, context.ShardIndex);
                    }

                    DamperMetrics.ParkedForRetryCounter.Add(1);
                    await context.ParkForRetryAsync(envelope, eventArgs.DeliveryTag);
                    _contextPool.Return(ackContext);
                }
            }
            catch (ArgumentNullException aex)
            {
                if (ackContext != null) _contextPool.Return(ackContext);
                _log.Error($"<==== While attempting to process message - argument is null - unable to proceed. | ERR MSG: {aex.Message}");
                throw;
            }
            catch (ChannelClosedException)
            {
                _log.Warn("<==== Attempted to write to a closed channel for customer - resuming customer | CUST ID: {Id}.", envelope.CustomerId);
                _channelRegistry.ResumeCustomer(envelope.CustomerId);
                throw;
            }
            catch (OperationCanceledException)
            {
                _log.Warn($"<==== ShardMessageProcessor: Shutdown requested | SHARD: {context.ShardIndex}");
                return;
            }
            catch (Exception ex)
            {
                if (ackContext != null) { _contextPool.Return(ackContext); }
                _log.Error(ex, "<==== Fatal error on shard parsing layer - NACKing message. | SHARD: {Idx}", context.ShardIndex);
                await context.NackAsync(eventArgs.DeliveryTag, multiple: false, requeue: true);
            }
        }

        private static Dictionary<string, string> GetEnvelopeHeaders(IDictionary<string, object?> amqpHeaders)
        {
            var envelopeHeaders = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string headerValueString = string.Empty;
            foreach (var (key, value) in amqpHeaders)
            {
                if (key.StartsWith(DamperConstants.DAMPER_HEADER_PREFIX) && value is byte[] headerValueBytes)
                {
                    headerValueString = Encoding.UTF8.GetString(headerValueBytes);
                    envelopeHeaders[key[DamperConstants.DAMPER_HEADER_PREFIX.Length..]] = headerValueString;
                    _traceLog.Trace($"Envelope header: KEY: {key} | VALUE: {headerValueString}");
                }
            }

            return envelopeHeaders;
        }
    }

    public static class ShardMessageProcessorExtensions
    {
        public static string GetStringHeader(this IDictionary<string, object?> amqpHeaders, string key)
        {
            return amqpHeaders.TryGetValue(key, out var val) && val is byte[] bytes
                   ? Encoding.UTF8.GetString(bytes)
                   : string.Empty;
        }
    }
}