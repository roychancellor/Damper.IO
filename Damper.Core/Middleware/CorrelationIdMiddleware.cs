using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Damper.Core.Utilities;
using Damper.Infrastructure.ReferenceData;

namespace Damper.Core.Middleware;

public class CorrelationIdMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate _next = next;

    // This middleware is at the start of the HTTP pipeline to create
    // a correlation ID for the request that will travel with the life
    // of the request for logging and other correlation purposes.
    public async Task InvokeAsync(HttpContext context, ILogger<CorrelationIdMiddleware> logger)
    {
        // Check if the calling system already sent a Correlation ID in the Request Headers
        if (!context.Request.Headers.TryGetValue(DamperConstants.REQUEST_X_CORRELATION_ID, out var correlationId))
        {
            // If not, generate a cryptographically secure 10-character token
            correlationId = CorrelationIdGenerator.Generate();
            
            // Inject it into the Request Headers so downstream pieces see it natively
            context.Request.Headers[DamperConstants.REQUEST_X_CORRELATION_ID] = correlationId;
        }

        // Mirror it to the Response Headers so the client gets the receipt
        context.Response.Headers[DamperConstants.REQUEST_X_CORRELATION_ID] = correlationId;

        // Stash it in HttpContext.Items for lightning-fast, typed access in other methods
        context.Items[DamperConstants.REQUEST_CORRELATION_ID] = correlationId.ToString();

        // Lock it into the async logging scope for NLog
        using (logger.BeginScope(new Dictionary<string, object> { [DamperConstants.REQUEST_CORRELATION_ID] = correlationId.ToString() }))
        {
            await _next(context);
        }
    } // The Scope automatically disposes here cleanly when the web request ends
}