using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Damper.Core.Utilities;

namespace Damper.Core.Middleware;

public class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    // This middleware is at the start of the HTTP pipeline to create
    // a correlation ID for the request that will travel with the life
    // of the request for logging and other correlation purposes.
    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        // Check if the calling system already sent a Tracking ID in the Request Headers
        if (!context.Request.Headers.TryGetValue("X-Correlation-ID", out var correlationId))
        {
            // If not, generate your cryptographically secure 10-character token
            correlationId = CorrelationIdGenerator.Generate();
            
            // Inject it into the Request Headers so downstream pieces see it natively
            context.Request.Headers["X-Correlation-ID"] = correlationId;
        }

        // Mirror it to the Response Headers so the client gets the receipt
        context.Response.Headers["X-Correlation-ID"] = correlationId;

        // Stash it in HttpContext.Items for lightning-fast, typed access in other methods
        context.Items["CorrelationId"] = correlationId.ToString();

        // Lock it into the async logging scope for NLog
        using (logger.BeginScope(new Dictionary<string, object> { ["CorrelationId"] = correlationId.ToString() }))
        {
            await _next(context);
        }
    } // The Scope automatically disposes here cleanly when the web request ends
}