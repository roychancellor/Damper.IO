using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Logging;

namespace Damper.Infrastructure.Logging;
public static class LoggerExtensions
{
    public static void Trace(this ILogger logger, string message, params object?[] args) 
        => logger.LogTrace(message, args);

    public static void Debug(this ILogger logger, string message, params object?[] args) 
        => logger.LogDebug(message, args);

    public static void Info(this ILogger logger, string message, params object?[] args) 
        => logger.LogInformation(message, args);

    public static void Warn(this ILogger logger, string message, params object?[] args) 
        => logger.LogWarning(message, args);

    public static void Error(this ILogger logger, string message, params object?[] args) 
        => logger.LogError(message, args);

    public static void Error(this ILogger logger, Exception ex, string message, params object?[] args) 
        => logger.LogError(ex, message, args);

    public static void Fatal(this ILogger logger, string message, params object?[] args) 
        => logger.LogCritical(message, args);
    
    public static void Fatal(this ILogger logger, Exception ex, string message, params object?[] args) 
        => logger.LogCritical(ex, message, args);

    public static IDisposable? BeginCorrelationScope(this ILogger logger, string correlationId, long integId, string integName)
        => logger.BeginScope(new Dictionary<string, object>
        {
            [DamperConstants.REQUEST_CORRELATION_ID] = correlationId,
            [DamperConstants.REQUEST_INTEGRATION_ID] = integId,
            [DamperConstants.REQUEST_INTEGRATION_NAME] = integName,
        });
}