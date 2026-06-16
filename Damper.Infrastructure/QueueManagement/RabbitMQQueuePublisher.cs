using RabbitMQ.Client;
using System.Text;

namespace Damper.Infrastructure.QueueManagement
{
    public class RabbitMQQueuePublisher : IQueuePublisher, IDisposable
    {
        private IChannel? _rabbitChannel;
        private IConnection _rabbitConnection;
        private readonly SemaphoreSlim _channelSemaphore = new(1, 1);
        private readonly string _exchangeName;
        private bool _disposed;

        public RabbitMQQueuePublisher(IConnection rabbitConnection, string exchangeName)
        {
            _rabbitConnection = rabbitConnection ?? throw new ArgumentNullException(nameof(rabbitConnection));
            _exchangeName = exchangeName;
        }

        public async Task<bool> PublishAsync(string correlationId, string customerId, string toPublish, CancellationToken ct, bool shouldThrow)
        {
            try
            {
                if (string.IsNullOrEmpty(customerId))
                {
                    throw new ArgumentException("Customer ID cannot be empty.", nameof(customerId));
                }
                
                // Lazily initialize the channel for this HTTP request scope if it doesn't exist
                await _channelSemaphore.WaitAsync();
                if (_rabbitChannel == null || !_rabbitChannel.IsOpen)
                {
                    if (_rabbitChannel != null)
                    {
                        await _rabbitChannel.DisposeAsync();
                    }

                    var channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

                    _rabbitChannel = await _rabbitConnection.CreateChannelAsync(channelOptions, cancellationToken: ct);
                }
                var bodyBytes = Encoding.UTF8.GetBytes(toPublish);
                
                // Modern v7+ Properties Setup: Flawless async delivery tracking
                var properties = new BasicProperties
                {
                    ContentType = "application/json",
                    ContentEncoding = "utf-8",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = correlationId,
                    Headers = new Dictionary<string, object?>
                    {
                        { "CustomerId", customerId }
                    },
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                };
    
                // Modern v7+ async publishing pattern
                await _rabbitChannel.BasicPublishAsync(
                    exchange: _exchangeName,
                    routingKey: customerId,
                    mandatory: true,
                    basicProperties: properties,
                    body: bodyBytes,
                    cancellationToken: ct
                );
                return true;
            }
            catch (Exception ex)
            {
                if (shouldThrow)
                {
                    throw new WebhookPublishException($"Fatal publish failure | CUSTOMER ID: {customerId} | CORRELATION ID: {correlationId}.", ex);
                }
                return false;
            }
            finally
            {
                _channelSemaphore.Release();
            }
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
            
            if (_disposed) { return; };
            _disposed = true;

            _rabbitChannel?.CloseAsync().GetAwaiter().GetResult();
            _rabbitChannel?.Dispose();
            _channelSemaphore.Dispose();
        }
    }

    public class WebhookPublishException : Exception
    {
        public WebhookPublishException(string message, Exception innerException) : base(message, innerException) { }
    }
}