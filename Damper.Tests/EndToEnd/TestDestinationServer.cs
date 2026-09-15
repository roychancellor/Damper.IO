using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Concurrent;

namespace Damper.Tests.EndToEnd;

internal sealed class TestDestinationServer : IAsyncDisposable
{
    private readonly ConcurrentQueue<ReceivedRequest> _requests = new();
    private readonly SemaphoreSlim _requestSignal = new(0);
    private int _requestCount;

    public int RequestCount => Volatile.Read(ref _requestCount);

    private WebApplication? _app;

    public int ResponseStatusCode { get; set; } = StatusCodes.Status200OK;

    public Uri Uri { get; private set; } = null!;

    internal sealed record ReceivedRequest(byte[] Body, string? ContentType);

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        var builder = WebApplication.CreateBuilder();

        builder.WebHost.UseUrls("http://127.0.0.1:0");

        _app = builder.Build();

        _app.MapPost("/", async context =>
        {
            using var memory = new MemoryStream();
            await context.Request.Body.CopyToAsync(memory, context.RequestAborted);

            Interlocked.Increment(ref _requestCount);

            _requests.Enqueue(new ReceivedRequest(memory.ToArray(), context.Request.ContentType));

            _requestSignal.Release();

            context.Response.StatusCode = ResponseStatusCode;
        });

        await _app.StartAsync(cancellationToken);

        var server = _app.Services.GetRequiredService<IServer>();
        var addresses = server.Features.Get<IServerAddressesFeature>();

        var address = addresses?.Addresses.Single()
            ?? throw new InvalidOperationException("Unable to determine test destination address.");

        Uri = new Uri(address);
    }

    public async Task<ReceivedRequest> WaitForRequestAsync(
        TimeSpan timeout,
        CancellationToken cancellationToken = default)
    {
        if (!await _requestSignal.WaitAsync(timeout, cancellationToken))
        {
            throw new TimeoutException($"The destination did not receive a request within {timeout}.");
        }

        if (!_requests.TryDequeue(out var request))
        {
            throw new InvalidOperationException("Destination request signal fired without a request.");
        }

        return request;
    }

    public async ValueTask DisposeAsync()
    {
        if (_app != null)
        {
            await _app.DisposeAsync();
        }

        _requestSignal.Dispose();
    }
}