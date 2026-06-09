using RabbitMQ.Client;
using System.Text;

namespace Damper.Infrastructure.QueueManagement
{
    public class RabbitMQQueuePublisher : IQueuePublisher
    {
        private IChannel? _rabbitChannel;
        private IConnection _rabbitConnection;

        public RabbitMQQueuePublisher(IConnection rabbitConnection)
        {
            _rabbitConnection = rabbitConnection ?? throw new ArgumentNullException(nameof(rabbitConnection));
        }

    public async Task<bool> PublishAsync(string customerId, string toPublish)
        {
            try
            {
                // Lazily initialize the channel for this HTTP request scope if it doesn't exist
                if (_rabbitChannel is null)
                {
                    _rabbitChannel = await _rabbitConnection.CreateChannelAsync();
                }
                var bodyBytes = Encoding.UTF8.GetBytes(toPublish);
                
                // Modern v7+ Properties Setup: Flawless async delivery tracking
                var properties = new BasicProperties
                {
                    DeliveryMode = DeliveryModes.Persistent, // Equivalent to old Persistent = true
                    Headers = new Dictionary<string, object?>
                    {
                        { "CustomerId", customerId }
                    }
                };
    
                // Modern v7+ async publishing pattern
                await _rabbitChannel.BasicPublishAsync(
                    exchange: "damper.webhook.exchange",
                    routingKey: $"webhook.shard.{customerId}",
                    mandatory: true,
                    basicProperties: properties,
                    body: bodyBytes
                );
                return true;
            }
            catch (Exception)
            {
                return false;
                //throw;
            }
        }
    }
}