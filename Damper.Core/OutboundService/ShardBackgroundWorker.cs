using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;

namespace Damper.Core.OutboundService
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

        public ShardBackgroundWorker(IConnection connection, int shardIndex, IShardMessageProcessor messageProcessor)
        {
            _connection = connection;
            _shardIndex = shardIndex;
            _messageProcessor = messageProcessor;
        }
        
        // This is infrastructure and should NOT be unit tested. Instead, integration test using Testcontainers
        // with an ephemeral Rabbit MQ instance.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Yield immediately to let the .NET Host startup loop process the other 15 shards without waiting
            await Task.Yield();
            
            _appLog.Info($"Configuring shard background worker | SHARD INDEX: {_shardIndex}");
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            //TODO: Put the Rabbit MQ exchange and queue names into appsettings
            var queueName = $"damper.webhook.queue.shard_{_shardIndex:D2}";
            var dlxName = "damper.dlx";
            var dlqName = "damper.webhook.queue.dead_letter";

            try
            {
                // Verify the infrastructure exists (Fail-fast if missing)
                await _channel.ExchangeDeclarePassiveAsync(dlxName, cancellationToken: stoppingToken);
                await _channel.QueueDeclarePassiveAsync(dlqName, cancellationToken: stoppingToken);
                await _channel.QueueDeclarePassiveAsync(queueName, cancellationToken: stoppingToken);
            }
            catch (OperationInterruptedException ex)
            {
                _appLog.Error(ex, "Required RabbitMQ infrastructure is missing for shard {Index:D2}. Pre-provision queues and exchanges.", _shardIndex);
                throw; // Stop the service if dependencies are missing
            }

            // TODO: Put the prefetch count in appsettings
            await _channel.BasicQosAsync(0, 30, false, stoppingToken);

            // Build the bridge context inside the runtime loop execution thread
            var processingContext = new RuntimeProcessingContext(_shardIndex, _channel, stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (sender, ea) => _messageProcessor.ProcessMessageAsync(ea, processingContext);

            await _channel.BasicConsumeAsync(queueName, autoAck: false, consumer, stoppingToken);
            _appLog.Info("Shard consumer thread bound to queue | SHARD {Index:D2} --> QUEUE {QueueName}", _shardIndex, queueName);
            
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        // Concrete wrapper implementation passed forward inside infrastructure layer
        private sealed class RuntimeProcessingContext : IShardProcessingContext
        {
            private readonly IChannel _channel;
            public int ShardIndex { get; }
            private CancellationToken _stoppingToken;
            public CancellationToken StoppingToken => _stoppingToken;

            public RuntimeProcessingContext(int shardIndex, IChannel channel, CancellationToken stoppingToken)
            {
                ShardIndex = shardIndex;
                _channel = channel;
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
                    _reqLog.Error($"CANNOT NACK: CHANNEL IS CLOSED!!!");
                    throw new InvalidOperationException("Cannot Nack: Channel is closed.");
                }
            }
            public async Task NackAsync(ulong deliveryTag, bool multiple, bool requeue) => await _channel.BasicNackAsync(deliveryTag, multiple, requeue);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_channel is not null) { await _channel.CloseAsync(cancellationToken); }
            await base.StopAsync(cancellationToken);
        }
    }
}