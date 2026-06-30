using System.Text;
using System.Text.Json;
using Damper.Infrastructure.ChannelRegistry;
using Damper.Infrastructure.CustomerChannels;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Damper.Core.OutboundService
{
    public class ShardBackgroundWorker : BackgroundService
    {
        private readonly int _shardIndex;
        private readonly IChannelRegistry _channelRegistry;
        private static readonly ILogger _log = Loggers.Request;
        private static readonly ILogger _appLog = Loggers.Application;
        
        private IConnection? _connection;
        private IChannel? _channel;
        private string? _queueName;

        public ShardBackgroundWorker(int shardIndex, IChannelRegistry channelRegistry)
        {
            _shardIndex = shardIndex;
            _channelRegistry = channelRegistry;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var factory = new ConnectionFactory { HostName = "localhost" };
            _connection = await factory.CreateConnectionAsync(stoppingToken);
            _channel = await _connection.CreateChannelAsync(cancellationToken: stoppingToken);

            _queueName = $"damper.webhook.queue.shard_{_shardIndex:D2}";

            // Enforce QoS prefetch to maintain fair-share across shared customer sharding
            await _channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 30, global: false, cancellationToken: stoppingToken);

            var consumer = new AsyncEventingBasicConsumer(_channel);
            consumer.ReceivedAsync += HandleIncomingMessageAsync;

            await _channel.BasicConsumeAsync(
                queue: _queueName,
                autoAck: false, // Explicit manual acknowledgment control
                consumer: consumer,
                cancellationToken: stoppingToken
            );

            _appLog.Info("Shard consumer thread {Index:D2} bound to {QueueName}", _shardIndex, _queueName);
            await Task.Delay(Timeout.Infinite, stoppingToken);
        }

        private async Task HandleIncomingMessageAsync(object sender, BasicDeliverEventArgs ea)
        {
            try
            {
                var bodyBytes = ea.Body.ToArray();
                var jsonString = Encoding.UTF8.GetString(bodyBytes);

                var envelope = JsonSerializer.Deserialize<WebhookEnvelope>(jsonString);
                if (envelope is null)
                {
                    await _channel!.BasicRejectAsync(ea.DeliveryTag, requeue: false);
                    return;
                }

                envelope.DeliveryTag = ea.DeliveryTag;
                
                // Inline callback hook assignment matching your WebhookEnvelope structure
                envelope.OnProcessingCompleteAsync = async () =>
                {
                    try
                    {
                        // Execute the final ACK once the dispatcher has completely finished delivery cycles
                        await _channel!.BasicAckAsync(ea.DeliveryTag, multiple: false);
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "Failed to ACK delivery tag {Tag} on Shard {Idx}", ea.DeliveryTag, _shardIndex);
                    }
                };

                // Hand off strictly using the customer ID. The registry handles the cache hydration via the Repository.
                var writer = await _channelRegistry.GetOrCreateChannel(envelope.CustomerId);
                await writer.WriteAsync(envelope);
            }
            catch (Exception ex)
            {
                _log.Error(ex, "Fatal error on shard parsing layer {Idx}. NACKing message.", _shardIndex);
                await _channel!.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true);
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_channel is not null) await _channel.CloseAsync(cancellationToken);
            if (_connection is not null) await _connection.CloseAsync(cancellationToken);
            await base.StopAsync(cancellationToken);
        }
    }
}