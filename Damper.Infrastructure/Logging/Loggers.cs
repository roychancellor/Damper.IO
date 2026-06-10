using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Damper.Infrastructure.Logging;

public static class Loggers
{
  private static ILoggerFactory? _factory;

    public static void Initialize(ILoggerFactory factory) => _factory = factory;

    public static ILogger Application => _factory?.CreateLogger("Application") ?? NullLogger.Instance;
    public static ILogger Request => _factory?.CreateLogger("Request") ?? NullLogger.Instance;
    public static ILogger Health => _factory?.CreateLogger("Health") ?? NullLogger.Instance;
}
