using System.Collections.Concurrent;
using System.Threading.Channels;
using Damper.Infrastructure.ChannelRegistry;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Models;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Net.Http;
using Microsoft.Extensions.Hosting;

namespace Damper.Infrastructure.CustomerChannels
{
    public class CustomerChannelRegistry : IChannelRegistry
    {
        private static readonly ILogger _log = Loggers.Request;

        private readonly ConcurrentDictionary<string, Channel<WebhookEnvelope>> _registry = new();
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly CancellationToken _ct;
        private readonly IServiceScopeFactory _scopeFactory; // The standard lifecycle bridge

        public CustomerChannelRegistry(IHttpClientFactory httpClientFactory, IServiceScopeFactory scopeFactory, IHostApplicationLifetime appLifetime)
        {
            _httpClientFactory = httpClientFactory;
            _scopeFactory = scopeFactory;
            _ct = appLifetime.ApplicationStopping;
        }

        public async Task<ChannelWriter<WebhookEnvelope>> GetOrCreateChannel(string customerId)
        {
            if (_registry.TryGetValue(customerId, out var existingChannel))
            {
                _log.Debug($"Channel registry HIT for customer ID {customerId} - returning writer immediately");
                return existingChannel.Writer;
            }

            // Open a scope to safely consume the scoped repository
            CustomerConfig? currentConfig;
            using (var scope = _scopeFactory.CreateScope())
            {
                _log.Debug($"Channel registry MISS for customer ID {customerId} - getting customer repository and retrieving customer config.");
                var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
                currentConfig = await repo.GetByIdAsync(customerId, _ct);
            } // The scope ends here, cleaning up any database contexts instantly

            if (currentConfig == null)
            {
                var msg = $"Configuration missing for customer: {customerId}";
                _log.Error($"While attempting to get customer config from repository - {msg}");
                throw new InvalidOperationException(msg);
            }
            
            return _registry.GetOrAdd(customerId, BuildCustomerChannel).Writer;
        }

        private Channel<WebhookEnvelope> BuildCustomerChannel(string customerId)
        {
            _log.Debug($"Creating/starting channel and adding to registry for customer ID {customerId}");
            var channelOptions = new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.Wait, // will create backpressure
                SingleWriter = false,
                SingleReader = true
            };

            var channel = Channel.CreateBounded<WebhookEnvelope>(channelOptions);

            // Kick off the long-running trickle sender loop
            _ = Task.Run(() => StartChannelDispatcherAsync(customerId, channel.Reader, _ct));

            _log.Info("Channel Registry: Initialized isolated egress valve for Customer {CustomerId}", customerId);
            return channel;
        }

        private async Task StartChannelDispatcherAsync(string customerId, ChannelReader<WebhookEnvelope> reader, CancellationToken ct)
        {
            // Pass the repository along to the dispatcher so it can re-query fresh config definitions on the fly
            var dispatcher = new ChannelDispatcher(_httpClientFactory, customerId, reader, _scopeFactory, ct);
            await dispatcher.RunLoopAsync(ct);
        }
    }
}