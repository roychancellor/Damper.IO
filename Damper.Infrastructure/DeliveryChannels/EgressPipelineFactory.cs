using System.Threading.Channels;
using Damper.Infrastructure.CustomerChannels;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.MessageTransport;
using Damper.Infrastructure.ReferenceData;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;

namespace Damper.Infrastructure.DeliveryChannels
{
    public class EgressPipelineFactory : IEgressPipelineFactory
    {
        private static ILogger _log = Loggers.Request;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ObjectPool<MessageAckContext> _contextPool;
        private readonly IOptionsMonitor<AppSettings> _optMon;

        public EgressPipelineFactory(IHttpClientFactory httpClientFactory,
                                     IServiceScopeFactory scopeFactory,
                                     ObjectPool<MessageAckContext> contextPool,
                                     IOptionsMonitor<AppSettings> optMon)
        {
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
            _contextPool = contextPool;
            _optMon = optMon;
        }

        public EgressPipeline CreatePipeline(CustomerConfig customerConfig, Action<string> onSuspensionTriggered, CancellationToken ct)
        {
            var bufferSize = customerConfig.MaxQueueCapacity > 0
                           ? customerConfig.MaxQueueCapacity
                           : _optMon.CurrentValue.RabbitMqSettings.DefaultMaxQueueCapacity;
            
            var channelOptions = new BoundedChannelOptions(bufferSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            };

            var channel = Channel.CreateBounded<MessageEnvelope>(channelOptions);

            // Explicitly start the dispatcher background worker here
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var backgroundTask = Task.Run(async () =>
            {
                try
                {
                    var dispatcher = new ChannelDispatcher(_optMon,
                                                           _httpClientFactory,
                                                           onSuspensionTriggered,
                                                           customerConfig,
                                                           channel.Reader,
                                                           _scopeFactory,
                                                           _contextPool,
                                                           ct);
                    await dispatcher.RunLoopAsync(ct);
                    tcs.SetResult(true);
                }
                catch (OperationCanceledException ocex) 
                { 
                    tcs.SetException(ocex);
                    _log.Info($"Dispatcher loop cancelled for customer | CUST ID: {customerConfig.CustomerId}"); 
                }
                catch (Exception ex) 
                {
                    // CRITICAL: If the loop dies, the pipeline is effectively broken.
                    // Log and trigger a failure state for the customer.
                    _log.Error(ex, $"CRITICAL: Dispatcher loop faulted for customer | CUST ID: {customerConfig.CustomerId}");
                    tcs.SetException(ex);
                    
                    // CRITICAL: Close the valve so no more messages can be 'lost'
                    channel.Writer.TryComplete(ex);
                    
                    onSuspensionTriggered(customerConfig.CustomerId); 
                }
            }, ct);

            return new EgressPipeline(channel.Writer, tcs.Task, CancellationTokenSource.CreateLinkedTokenSource(ct));
        }
    }
}