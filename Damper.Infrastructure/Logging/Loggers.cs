using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Damper.Infrastructure.Logging;

public static class Loggers
{
    private static ILogger _application = NullLogger.Instance;
    private static ILogger _request = NullLogger.Instance;
    private static ILogger _health = NullLogger.Instance;
    private static ILogger _requestTrace = NullLogger.Instance;
    
    public static void Initialize(ILoggerFactory factory)
    {
      _application = factory.CreateLogger("Application");
      _request = factory.CreateLogger("Request");
      _health = factory.CreateLogger("Health");
      _requestTrace = factory.CreateLogger("RequestTrace");
    }

    public static ILogger Application => _application;
    public static ILogger Request => _request;
    public static ILogger Health => _health;
    public static ILogger RequestTrace => _requestTrace;
}
