using System.Threading.Channels;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.ObjectPool;

namespace Damper.Infrastructure.CustomerChannels
{
    public class CustomerEgressPipelineFactory : IEgressPipelineFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ObjectPool<WebhookAckContext> _contextPool;

        public CustomerEgressPipelineFactory(IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory, ObjectPool<WebhookAckContext> contextPool)
        {
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
            _contextPool = contextPool;
        }

        public CustomerEgressPipeline CreatePipeline(CustomerConfig customerConfig, Action<string> onSuspensionTriggered, CancellationToken ct)
        {
            var bufferSize = customerConfig.MaxQueueCapacity > 0 ? customerConfig.MaxQueueCapacity : 5000;
            
            var channelOptions = new BoundedChannelOptions(bufferSize)
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleWriter = false,
                SingleReader = true
            };

            var channel = Channel.CreateBounded<WebhookEnvelope>(channelOptions);

            // Explicitly start the dispatcher background worker here
            var backgroundTask = Task.Run(async () =>
            {
                var dispatcher = new ChannelDispatcher(_httpClientFactory,
                                                       onSuspensionTriggered,
                                                       customerConfig,
                                                       channel.Reader,
                                                       _scopeFactory,
                                                       _contextPool,
                                                       ct);
                await dispatcher.RunLoopAsync(ct);
            }, ct);

            return new CustomerEgressPipeline(channel.Writer, backgroundTask);
        }
    }
}