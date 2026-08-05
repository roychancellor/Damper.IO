using System.Text;
using System.Threading.Channels;
using Damper.Domain.Common;
using Damper.Infrastructure.DeliveryChannels;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.MessageTransport;
using Damper.Infrastructure.Observability;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using RabbitMQ.Client.Events;

namespace Damper.Core.MessageProcessing
{
    public class ShardMessageProcessor : IShardMessageProcessor
    {
        private static readonly ILogger _log = Loggers.Request;
        private static readonly ILogger _traceLog = Loggers.RequestTrace;
        private readonly IChannelRegistry _channelRegistry;
        private readonly ObjectPool<MessageAckContext> _contextPool;
        private readonly IOptionsMonitor<AppSettings> _optMon;

        public ShardMessageProcessor(IChannelRegistry channelRegistry, ObjectPool<MessageAckContext> contextPool, IOptionsMonitor<AppSettings> optMon)
        {
            _channelRegistry = channelRegistry;
            _contextPool = contextPool;
            _optMon = optMon;
        }

        public async Task ProcessMessageAsync(BasicDeliverEventArgs eventArgs, IShardProcessingContext context)
        {
            MessageAckContext? ackContext = null;
            MessageEnvelope envelope = new();
            try
            {
                ArgumentNullException.ThrowIfNull(context, nameof(context));

                _log.Info($"====> Processing new message | SHARD INDEX: {context.ShardIndex}");
                _traceLog.Trace($"====> ProcessMessageAsync: Processing binary message | SHARD INDEX: {context.ShardIndex}");
                var amqpHeaders = eventArgs.BasicProperties.Headers;
                if (amqpHeaders == null)
                {
                    _log.Error($"Consumed message is missing AMQP headers - Rejecting to DLQ. | SHARD INDEX: {context.ShardIndex}");
                    DamperMetrics.SentToDeadLetter.Add(1);
                    await context.RejectAsync(eventArgs.DeliveryTag, requeue: false);
                    return;
                }
                // Metadata about the message comes from the RabbitMQ message headers
                var correlationId = new CorrelationId(amqpHeaders.GetStringHeader(DamperConstants.X_DAMPER_CORRELATION_ID));
                var apiKey = new ApiKey(amqpHeaders.GetStringHeader(DamperConstants.REQUEST_X_DAMPER_API_KEY));
                var integIdStr = amqpHeaders.GetStringHeader(DamperConstants.X_DAMPER_INTEGRATION_ID);
                var integId = Convert.ToInt64(integIdStr);
                var integName = new IntegrationName(amqpHeaders.GetStringHeader(DamperConstants.X_DAMPER_INTEGRATION_NAME));
                var destinationUrl = amqpHeaders.GetStringHeader(DamperConstants.X_DAMPER_DESTINATION_URL);
                int attemptCount = amqpHeaders.TryGetValue(DamperConstants.X_DAMPER_ATTEMPT_COUNT, out var attemptObj)
                                   ? Convert.ToInt32(attemptObj)
                                   : 1;

                // Allow NLog to automatically populate transaction metadata in every log statement within this method beyond this point
                using var correlationScope = _log.BeginCorrelationScope(correlationId.Value, integId, integName.Value);

                _traceLog.Trace($"Retrieved message headers | CORR ID: {correlationId} | API KEY: REDACTED | INTEG ID: {integId} | INTEG NAME: {integName} | DEST URL: {destinationUrl} | ATTEMPT: {attemptCount}");
                
                _traceLog.Trace($"Getting HTTP headers for the original request from the Rabbit MQ headers");
                var envelopeHeaders = GetEnvelopeHeaders(amqpHeaders);

                _traceLog.Trace($"Building new MessageEnvelope and settings is AckContext property (retrieved from pool)");
                ackContext = _contextPool.Get();
                ackContext.DeliveryTag = eventArgs.DeliveryTag;
                ackContext.ShardIndex = context.ShardIndex;
                ackContext.ShardContext = context;
                
                envelope = new MessageEnvelope
                {
                    CorrelationId = correlationId,
                    IntegrationId = integId,
                    IntegrationName = integName,
                    DestinationUrl = destinationUrl,
                    Headers = envelopeHeaders,
                    AttemptCount = attemptCount,
                    ReceivedAt = DateTime.UtcNow,
                    RawPayloadBytes = eventArgs.Body,
                    AckContext = ackContext,
                    CancelToken = context.StoppingToken,
                    ShouldThrow = true,
                };

                _traceLog.Trace($"Getting message delivery channel | DEL TAG: {eventArgs.DeliveryTag}");
                var pipeline = await _channelRegistry.GetOrCreatePipelineAsync(integId);

                _traceLog.Trace($"Attempting non-blocking write to delivery channel | DEL TAG: {eventArgs.DeliveryTag}");

                var canWrite = await pipeline.Writer.WaitToWriteAsync(
                                        new CancellationTokenSource(_optMon.CurrentValue.ProcessorSettings.WaitToWriteExpirationMillis).Token);
                if (canWrite && !pipeline.BackgroundTask.IsCompleted && pipeline.Writer.TryWrite(envelope))
                {
                    // DO NOT ACK HERE!!! LET THE CHANNEL DISPATCHER HANDLE THE ACK WHEN IT KNOWS THE OUTCOME
                    _traceLog.Trace($"<==== ProcessMessageAsync: Successfully enqueued envelope in channel | DELIVERY TAG: {eventArgs.DeliveryTag}");
                    _log.Info($"<==== Successfully enqueued envelope in channel | DELIVERY TAG: {eventArgs.DeliveryTag}");
                }
                else
                {
                    // Covers every reason we couldn't hand off: suspended (sentinel writer),
                    // buffer full, or a genuinely dead/crashed pipeline - all get parked for
                    // delayed automatic retry instead of an immediate, unthrottled NACK.
                    if (pipeline.BackgroundTask.IsCompleted && !_channelRegistry.IsSuspended(integId))
                    {
                        _log.Warn("<==== Pipeline crashed for delivery channel - Resetting registry.");
                        _channelRegistry.ResetPipeline(integId);
                    }
                    else
                    {
                        _log.Warn("<==== Delivery buffer full or suspended - Parking message for delayed retry | SHARD: {Idx}.", context.ShardIndex);
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
                _log.Warn("<==== Attempted to write to a closed channel for integration - resuming integration");
                _channelRegistry.ResumeIntegration(envelope.IntegrationId);
                throw;
            }
            catch (OperationCanceledException)
            {
                _log.Warn("<==== ShardMessageProcessor: Shutdown requested | SHARD: {Idx}", context.ShardIndex);
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