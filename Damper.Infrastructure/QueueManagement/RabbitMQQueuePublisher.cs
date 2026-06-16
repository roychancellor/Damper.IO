using RabbitMQ.Client;
using System.Text;

namespace Damper.Infrastructure.QueueManagement
{
    public class RabbitMQQueuePublisher : IQueuePublisher, IDisposable
    {
        private IConnection _connection;
        private IChannel? _channel;
        private readonly SemaphoreSlim _channelSemaphore = new(1, 1);
        private readonly string _exchangeName;
        private bool _disposed;

        public RabbitMQQueuePublisher(IConnection connection, string exchangeName)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _exchangeName = exchangeName;
        }

        public async Task<bool> PublishAsync(PublishWrapper pw)
        {
            try
            {
                if (string.IsNullOrEmpty(pw.CustomerId))
                {
                    throw new ArgumentNullException(nameof(pw), "Customer ID cannot be null or empty.");
                }
                
                // Lazily initialize the channel for this HTTP request scope if it doesn't exist
                await _channelSemaphore.WaitAsync();
                if (_channel == null || !_channel.IsOpen)
                {
                    if (_channel != null)
                    {
                        await _channel.DisposeAsync();
                    }

                    var channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

                    _channel = await _connection.CreateChannelAsync(channelOptions, pw.CancelToken);
                }
                var bodyBytes = Encoding.UTF8.GetBytes(pw.Payload);
                
                // Modern v7+ Properties Setup with async delivery tracking
                var properties = new BasicProperties
                {
                    ContentType = "application/json",
                    ContentEncoding = "utf-8",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = pw.CorrelationId,
                    Headers = new Dictionary<string, object?>
                    {
                        { nameof(pw.CustomerId), pw.CustomerId }
                    },
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                };
    
                // Modern v7+ async publishing pattern
                await _channel.BasicPublishAsync(
                    exchange: _exchangeName,
                    routingKey: pw.CustomerId,
                    mandatory: true,
                    basicProperties: properties,
                    body: bodyBytes,
                    cancellationToken: pw.CancelToken
                );
                return true;
            }
            catch (Exception ex)
            {
                if (pw.ShouldThrow)
                {
                    throw new WebhookPublishException($"Fatal publish failure | CUSTOMER ID: {pw.CustomerId} | CORRELATION ID: {pw.CorrelationId}.", ex);
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

            _channel?.CloseAsync().GetAwaiter().GetResult();
            _channel?.Dispose();
            _channelSemaphore.Dispose();
        }
    }

    public class WebhookPublishException : Exception
    {
        public WebhookPublishException(string message, Exception innerException) : base(message, innerException) { }
    }
}