using Damper.Infrastructure.Logging;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using System.Text;

namespace Damper.Infrastructure.QueueManagement
{
    public class RabbitMQQueuePublisher : IQueuePublisher, IDisposable
    {
        private static ILogger _log = Loggers.Request;
        private static ILogger _traceLog = Loggers.RequestTrace;
        private IConnection _connection;
        private IChannel? _channel;
        private readonly SemaphoreSlim _channelSemaphore = new(1, 1);
        private bool _disposed;
        private IOptionsMonitor<AppSettings> _appOptMon;

        public RabbitMQQueuePublisher(IConnection connection, IOptionsMonitor<AppSettings> appOptMon)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
            _appOptMon = appOptMon;
        }

        public async Task<bool> PublishAsync(PublishWrapper pw)
        {
            try
            {
                _traceLog.Trace($"Starting publish");
                if (pw == null)
                {
                    _log.Error($"While attempting to publish - passed in Publish Wrapper is NULL");
                    throw new ArgumentNullException(nameof(pw), "Publish Wrapper cannot be null.");
                }
                _traceLog.Trace($"Received Publish Wrapper: {pw}");
                if (!pw.IsValid(out string invalidMessage))
                {
                    var msg = $"PublishAsync - passed in Publish Wrapper is invalid | REASON: {invalidMessage}";
                    _traceLog.Error(msg);
                    throw new ArgumentNullException(nameof(pw), msg);
                }
                
                // Lazily initialize the channel for this HTTP request scope if it doesn't exist
                _traceLog.Trace($"Awaiting queue channel semaphore");
                await _channelSemaphore.WaitAsync();
                if (_channel == null || !_channel.IsOpen)
                {
                    if (_channel != null)
                    {
                        _traceLog.Trace($"Disposing of non-null, but non-open queue channel");
                        await _channel.DisposeAsync();
                    }

                    _traceLog.Trace($"Creating queue channel options - confirmations and tracking are enabled");
                    var channelOptions = new CreateChannelOptions(publisherConfirmationsEnabled: true, publisherConfirmationTrackingEnabled: true);

                    _traceLog.Trace($"Creating queue channel");
                    _channel = await _connection.CreateChannelAsync(channelOptions, pw.CancelToken);
                    _channel.BasicReturnAsync += OnBasicReturnAsync;
                }
                
                _traceLog.Trace($"Converting webhook envelope payload to bytes");
                var bodyBytes = pw.Payload.RawPayloadBytes;
                _traceLog.Trace($"NUM BYTES: {bodyBytes.Length}");
                
                // Modern v7+ Properties Setup with async delivery tracking
                _traceLog.Trace($"Creating Basic Properties object with headers");
                var properties = new BasicProperties
                {
                    ContentType = "application/json",
                    ContentEncoding = "utf-8",
                    DeliveryMode = DeliveryModes.Persistent,
                    MessageId = pw.CorrelationId,
                    Headers = new Dictionary<string, object?>
                    {
                        { "x-damper-correlation-id", pw.CorrelationId },
                        { "x-damper-customer-id", pw.CustomerId },
                        { "x-damper-destination-url", pw.Payload.DestinationUrl },
                        { "x-damper-attempt-count", pw.Payload.AttemptCount },
                    },
                    Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds()),
                };
                _traceLog.Trace($"Mapping wekhook request headers to queue message headers for binary transport");
                foreach (var header in pw.Payload.Headers)
                {
                    properties.Headers.Add($"h_{header.Key}", header.Value);
                }
    
                // Modern v7+ async publishing pattern
                _traceLog.Trace($"Publishing to exchange");
                await _channel.BasicPublishAsync(
                    exchange: _appOptMon.CurrentValue.RabbitMqSettings.ExchangeName,
                    routingKey: pw.CustomerId,
                    mandatory: true,
                    basicProperties: properties,
                    body: bodyBytes,
                    cancellationToken: pw.CancelToken
                );
                _traceLog.Trace($"Publish successful!");
                return true;
            }
            catch (Exception ex)
            {
                if (pw.ShouldThrow)
                {
                    var msg = $"Fatal publish failure | CUSTOMER ID: {pw.CustomerId}";
                    _traceLog.Error(msg, ex);
                    throw new WebhookPublishException(msg, ex);
                }
                _traceLog.Error($"Publish failed! (ShouldThrow = false)");
                return false;
            }
            finally
            {
                _traceLog.Trace($"Releasing channel semaphore");
                _channelSemaphore.Release();
            }
        }

        private Task OnBasicReturnAsync(object sender, BasicReturnEventArgs ea)
        {
            // Extract routing details
            var routingKey = ea.RoutingKey;
            var exchange = ea.Exchange;
            var replyCode = ea.ReplyCode; // e.g. 312 (NO_ROUTE)
            var replyText = ea.ReplyText;

            // Log the unroutable message error at the FATAL level so it is LOUD!!!
            _log.Fatal(
                "RabbitMQ message returned (unroutable) | CORR ID: {Corr} | CUST ID: {Key} | XCHG: {Exchange} | Code: {Code} | Text: {Text}",
                ea.BasicProperties.MessageId,
                routingKey,
                exchange,
                replyCode,
                replyText
            );

            // Yield back to complete the Task expected by AsyncEventHandler
            return Task.CompletedTask;
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