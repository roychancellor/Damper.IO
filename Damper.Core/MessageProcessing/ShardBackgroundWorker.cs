using Damper.Infrastructure.Logging;
using Damper.Infrastructure.MessageTransport;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Damper.Core.MessageProcessing
{
    public class ShardBackgroundWorker : BackgroundService
    {
        private static readonly ILogger _appLog = Loggers.Application;
        private static readonly ILogger _reqLog = Loggers.Request;
        private static readonly ILogger _traceLog = Loggers.RequestTrace;
       
        private readonly int _shardIndex;
        private readonly IShardMessageProcessor _messageProcessor;
        private IConnection _connection;
        private IChannel? _channel;
        private readonly IOptionsMonitor<AppSettings> _optMon;

        public ShardBackgroundWorker(IConnection connection, int shardIndex, IShardMessageProcessor messageProcessor, IOptionsMonitor<AppSettings> optMon)
        {
            _connection = connection;
            _shardIndex = shardIndex;
            _messageProcessor = messageProcessor;
            _optMon = optMon;
        }
        
        // This is infrastructure and should NOT be unit tested. Instead, integration test using Testcontainers
        // with an ephemeral Rabbit MQ instance.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Yield immediately to let the .NET Host startup loop process the other 15 shards without waiting
            await Task.Yield();
            
            _appLog.Info($"Configuring shard background worker | SHARD INDEX: {_shardIndex}");
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            var rmqData = _optMon.CurrentValue.RabbitMqSettings;
            var shardQueueName = $"{rmqData.IngressShardPrefix}{_shardIndex:D2}";
            var dlxName = rmqData.DeadLetterExchange;
            var dlqName = rmqData.DeadLetterQueue;
            var parkingXchgName = rmqData.ParkingLotExchange;
            var parkingQueueName = rmqData.ParkingLotQueue;

            try
            {
                // Verify the infrastructure exists (Fail-fast if missing)
                await _channel.ExchangeDeclarePassiveAsync(dlxName, cancellationToken: stoppingToken);
                await _channel.QueueDeclarePassiveAsync(dlqName, cancellationToken: stoppingToken);
                await _channel.QueueDeclarePassiveAsync(shardQueueName, cancellationToken: stoppingToken);
                await _channel.ExchangeDeclarePassiveAsync(parkingXchgName, cancellationToken: stoppingToken);
                await _channel.QueueDeclarePassiveAsync(parkingQueueName, cancellationToken: stoppingToken);
            }
            catch (OperationInterruptedException ex)
            {
                _appLog.Error(ex, "Required RabbitMQ infrastructure is missing for shard {Index:D2}. Pre-provision queues and exchanges.", _shardIndex);
                throw; // Stop the service if dependencies are missing
            }

            rmqData = _optMon.CurrentValue.RabbitMqSettings;
            await _channel.BasicQosAsync(rmqData.PrefetchSize, rmqData.PrefetchCount, rmqData.IsPrefetchGlobal, stoppingToken);

            // Build the bridge context inside the runtime loop execution thread
            var processingContext = new RuntimeProcessingContext(_shardIndex, _channel, _optMon, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (sender, eventArgs) => _messageProcessor.ProcessMessageAsync(eventArgs, processingContext);

            await _channel.BasicConsumeAsync(shardQueueName, autoAck: false, consumer, stoppingToken);
            _appLog.Info("Shard consumer thread bound to queue | SHARD {Index:D2} --> QUEUE {QueueName}", _shardIndex, shardQueueName);
            
            // This background task is meant to run the entirety of the application lifetime
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        // Concrete wrapper implementation passed forward inside infrastructure layer
        private sealed class RuntimeProcessingContext : IShardProcessingContext
        {
            private readonly IChannel _channel;
            public int ShardIndex { get; }
            private CancellationToken _stoppingToken;
            public CancellationToken StoppingToken => _stoppingToken;
            private readonly IOptionsMonitor<AppSettings> _optMon;

            public RuntimeProcessingContext(int shardIndex, IChannel channel, IOptionsMonitor<AppSettings> optMon, CancellationToken stoppingToken)
            {
                ShardIndex = shardIndex;
                _channel = channel;
                _optMon = optMon;
                _stoppingToken = stoppingToken;
            }

            public async Task AckAsync(ulong deliveryTag) => await _channel.BasicAckAsync(deliveryTag, multiple: false);
            public async Task RejectAsync(ulong deliveryTag, bool requeue) 
            {
                if (_channel.IsOpen)
                {
                    _traceLog.Trace($"Rejecting message! | DEL TAG: {deliveryTag} | REQUEUE: {requeue}");
                    await _channel.BasicRejectAsync(deliveryTag, requeue);
                }
                else
                {
                    _reqLog.Error($"CANNOT REJECT: CHANNEL IS CLOSED!!!");
                    throw new InvalidOperationException("Cannot Reject: Channel is closed.");
                }
            }
            public async Task NackAsync(ulong deliveryTag, bool multiple, bool requeue) => await _channel.BasicNackAsync(deliveryTag, multiple, requeue);
            public async Task ParkForRetryAsync(MessageEnvelope envelope, ulong deliveryTag)
            {
                // TODO: Create an extension method or some other common method for this for DRYness (refer to RabbitMQQueuePublisher)
                var headers = new Dictionary<string, object?>
                {
                    { DamperConstants.X_DAMPER_CORRELATION_ID, envelope.CorrelationId.Value },
                    { DamperConstants.X_DAMPER_INTEGRATION_ID, envelope.IntegrationId.ToString() },
                    { DamperConstants.X_DAMPER_INTEGRATION_NAME, envelope.IntegrationName.Value },
                    { DamperConstants.X_DAMPER_DESTINATION_URL, envelope.DestinationUrl },
                    { DamperConstants.X_DAMPER_ATTEMPT_COUNT, 1 }, // We want this to come back fresh and ready to retry sending
                };
                foreach (var (key, value) in envelope.Headers)
                {
                    headers[$"{DamperConstants.DAMPER_HEADER_PREFIX}{key}"] = value;
                }

                int ttlMs = GetTTLMillis();
                var props = new BasicProperties
                {
                    Persistent = true,
                    Expiration = ttlMs.ToString(),
                    Headers = headers
                };

                // Routing key MUST be the Integration ID, not the queue name — see comment
                // on the parking queue's dead-letter config for why this matters.
                await _channel.BasicPublishAsync(
                    exchange: _optMon.CurrentValue.RabbitMqSettings.ParkingLotExchange,
                    routingKey: envelope.IntegrationId.ToString(),
                    mandatory: false, // fanout with one queue - nothing to fail to route to
                    basicProperties: props,
                    body: envelope.RawPayloadBytes,
                    cancellationToken: envelope.CancelToken
                );

                await _channel.BasicAckAsync(deliveryTag, multiple: false);

                _reqLog.Info("Parked message for delayed retry | INTEG ID: {Id} | INTEG NAME: {Name} | TTL_MS: {Ttl}",
                                envelope.IntegrationId, envelope.IntegrationName, ttlMs);
            }

            private int GetTTLMillis()
            {
                return _optMon.CurrentValue.RabbitMqSettings.ParkingLotBaseTTLMillis
                       + Random.Shared.Next(0, _optMon.CurrentValue.RabbitMqSettings.ParkingLotJitterMillis);
            }
        }

        // Part of the .NET BackgroundTask infrastructure
        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_channel is not null)
            {
                await _channel.CloseAsync(cancellationToken);
            }
            await base.StopAsync(cancellationToken);
        }
    }
}