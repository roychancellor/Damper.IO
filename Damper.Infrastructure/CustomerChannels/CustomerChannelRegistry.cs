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
    private readonly ConcurrentDictionary<string, Channel<WebhookEnvelope>> _registry = new();
    private readonly IHttpClientFactory _httpClientFactory;
    private static readonly ILogger _log = Loggers.Request;
    private readonly CancellationToken _ct;
    private readonly IServiceScopeFactory _scopeFactory; // The standard lifecycle bridge

    public CustomerChannelRegistry(
        IHttpClientFactory httpClientFactory, 
        IServiceScopeFactory scopeFactory,
        IHostApplicationLifetime appLifetime)
    {
        _httpClientFactory = httpClientFactory;
        _scopeFactory = scopeFactory;
        _ct = appLifetime.ApplicationStopping;
    }

    public async Task<ChannelWriter<WebhookEnvelope>> GetOrCreateChannel(string customerId)
    {
        // 1. High-speed RAM optimization: If the channel exists, bypass scopes entirely
        if (_registry.TryGetValue(customerId, out var existingChannel))
        {
            return existingChannel.Writer;
        }
        
        CustomerConfig? currentConfig;

        // 2. Lifecycle Bridge: Open a scope to safely consume the scoped repository
        using (var scope = _scopeFactory.CreateScope())
        {
            var repo = scope.ServiceProvider.GetRequiredService<ICustomerRepository>();
            
            // This invokes your exact CachedCustomerRepository.GetByIdAsync method
            currentConfig = await repo.GetByIdAsync(customerId, _ct);
        } // The scope ends here, cleaning up any database contexts instantly

        if (currentConfig == null)
        {
            throw new InvalidOperationException($"Configuration missing for customer: {customerId}");
        }
        
        return _registry.GetOrAdd(customerId, id =>
        {
            var channelOptions = new BoundedChannelOptions(5000)
            {
                FullMode = BoundedChannelFullMode.Wait, // will create backpressure
                SingleWriter = false,
                SingleReader = true
            };

            var channel = Channel.CreateBounded<WebhookEnvelope>(channelOptions);

            // Kick off the long-running trickle sender loop
            _ = Task.Run(() => StartChannelDispatcherAsync(id, channel.Reader, _ct));

            _log.Info("Initialized isolated egress valve for Customer {CustomerId}", id);
            return channel;
        }).Writer;
    }

    private async Task StartChannelDispatcherAsync(string customerId, ChannelReader<WebhookEnvelope> reader, CancellationToken ct)
    {
        // Pass the repository along to the dispatcher so it can re-query fresh config definitions on the fly
        var dispatcher = new ChannelDispatcher(_httpClientFactory, customerId, reader, _scopeFactory, ct);
        await dispatcher.RunLoopAsync(ct);
    }
}
}