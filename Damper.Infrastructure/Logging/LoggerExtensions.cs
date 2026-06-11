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

    public static void Fatal(this ILogger logger, Exception ex, string message, params object?[] args) 
        => logger.LogCritical(ex, message, args);
}