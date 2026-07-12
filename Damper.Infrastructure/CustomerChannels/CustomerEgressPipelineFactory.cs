using System.Threading.Channels;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace Damper.Infrastructure.CustomerChannels
{
    public class CustomerEgressPipelineFactory : IEgressPipelineFactory
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IServiceScopeFactory _scopeFactory;

        public CustomerEgressPipelineFactory(IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory)
        {
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
        }

        public CustomerEgressPipeline CreatePipeline(CustomerConfig customerConfig, CancellationToken ct)
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
                var dispatcher = new ChannelDispatcher(_httpClientFactory, customerConfig, channel.Reader, _scopeFactory, ct);
                await dispatcher.RunLoopAsync(ct);
            }, ct);

            return new CustomerEgressPipeline(channel.Writer, backgroundTask);
        }
    }
}