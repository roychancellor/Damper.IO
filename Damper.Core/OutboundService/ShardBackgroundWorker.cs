using Damper.Infrastructure.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Damper.Core.OutboundService
{
    public class ShardBackgroundWorker : BackgroundService
    {
        private static readonly ILogger _appLog = Loggers.Application;
        
        private readonly int _shardIndex;
        private readonly IShardMessageProcessor _messageProcessor;
        private IConnection? _connection;
        private IChannel? _channel;

        public ShardBackgroundWorker(int shardIndex, IShardMessageProcessor messageProcessor)
        {
            _shardIndex = shardIndex;
            _messageProcessor = messageProcessor;
        }
        
        // This is infrastructure and should NOT be unit tested. Instead, integration test using Testcontainers
        // with an ephemeral Rabbit MQ instance.
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _appLog.Info($"Configuring shard background worker | SHARD INDEX: {_shardIndex}");
            var factory = new ConnectionFactory { HostName = "localhost" };
            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            var queueName = $"damper.webhook.queue.shard_{_shardIndex:D2}";
            await _channel.BasicQosAsync(0, 30, false, stoppingToken);

            // Build the bridge context inside the runtime loop execution thread
            var processingContext = new RuntimeProcessingContext(_shardIndex, _channel);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += (sender, ea) => _messageProcessor.ProcessMessageAsync(ea, processingContext);

            await _channel.BasicConsumeAsync(queueName, autoAck: false, consumer, stoppingToken);
            _appLog.Info("Shard consumer thread {Index:D2} bound to {QueueName}", _shardIndex, queueName);
            
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        // Concrete wrapper implementation passed forward inside infrastructure layer
        private sealed class RuntimeProcessingContext : IShardProcessingContext
        {
            private readonly IChannel _channel;
            public int ShardIndex { get; }

            public RuntimeProcessingContext(int shardIndex, IChannel channel)
            {
                ShardIndex = shardIndex;
                _channel = channel;
            }

            public async Task AckAsync(ulong deliveryTag) => await _channel.BasicAckAsync(deliveryTag, multiple: false);
            public async Task RejectAsync(ulong deliveryTag, bool requeue) => await _channel.BasicRejectAsync(deliveryTag, requeue);
            public async Task NackAsync(ulong deliveryTag, bool multiple, bool requeue) => await _channel.BasicNackAsync(deliveryTag, multiple, requeue);
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_channel is not null) await _channel.CloseAsync(cancellationToken);
            if (_connection is not null) await _connection.CloseAsync(cancellationToken);
            await base.StopAsync(cancellationToken);
        }
    }
}