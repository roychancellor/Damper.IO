using Damper.Core.IngestionService;
using Damper.Core.Middleware;
using Damper.Core.Utilities;
using Damper.Infrastructure.Extensions;
using Damper.Infrastructure.Logging;
using NLog.Web;
using NLog;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.ObjectPool;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using RabbitMQ.Client;
using Microsoft.Extensions.Options;
using Damper.Infrastructure.MessageTransport;
using Damper.Infrastructure.DeliveryChannels;
using Damper.Core.MessageProcessing;
using Microsoft.Extensions.Primitives;

var bootstrapLogger = LogManager.Setup().GetCurrentClassLogger();

try
{
    bootstrapLogger.Info($"DAMPER.IO APPLICATION STARTING");

    bootstrapLogger.Info($"Creating web application builder");
    var builder = WebApplication.CreateBuilder(args);

    bootstrapLogger.Info($"Binding application settings to application reference data object");
    var appSettingsSection = builder.Configuration.GetSection("ApplicationData");
    var appSettings = appSettingsSection.Get<AppSettings>() ?? new AppSettings();
    builder.Services.AddOptions<AppSettings>()
                    .Bind(appSettingsSection)
                    .PostConfigure(ard => ard.EgressSettings.SystemHeaders = new(ard.EgressSettings.SystemHeaders, StringComparer.OrdinalIgnoreCase))
                    .ValidateDataAnnotations()
                    .ValidateOnStart();
    
    bootstrapLogger.Info($"Setting NLog as the logging provider");
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();
    
    bootstrapLogger.Info($"Adding services to builder");
    builder.Services.AddRepositories()
                    .AddRabbitMqInfrastructure()
                    .AddQueuePublishing()
                    .AddMessageIngestion();
    builder.Services.AddSingleton<IChannelRegistry, DeliveryChannelRegistry>();
    builder.Services.AddSingleton<IShardMessageProcessor, ShardMessageProcessor>();
    builder.Services.AddSingleton<IEgressPipelineFactory, EgressPipelineFactory>();
    for (int i = 0; i < appSettings.RabbitMqSettings.NumberOfShards; i++)
    {
        int shardIndex = i;
        // Using the explicit generic registration ensures Microsoft.Extensions.Hosting 
        // correctly identifies and tracks each individual IHostedService instance.
        builder.Services.AddTransient<IHostedService>(sp =>
            new ShardBackgroundWorker(sp.GetRequiredService<IConnection>(),
                                      shardIndex,
                                      sp.GetRequiredService<IShardMessageProcessor>(),
                                      sp.GetRequiredService<IOptionsMonitor<AppSettings>>()));
    }
    var egressData = appSettings.EgressSettings;
    builder.Services.AddHttpClient(egressData.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
                    {
                        // Keep-alive timeouts protect against dead network pipes
                        PooledConnectionLifetime = TimeSpan.FromSeconds(egressData.PooledConnectionLifetimeSeconds), // FIXES DNS STAGNATION: Recycles sockets safely
                        PooledConnectionIdleTimeout = TimeSpan.FromSeconds(egressData.PooledConnectionIdleTimeoutSeconds),
                        
                        // Performance tuning for massive multi-tenant throughput
                        MaxConnectionsPerServer = egressData.MaxConnectionsPerServer, // Limits connections to any *single* integration domain
                        EnableMultipleHttp2Connections = egressData.EnableMultipleHttp2Connections // Enhances HTTP/2 streaming multiplexing efficiency
                    })
                    .SetHandlerLifetime(TimeSpan.FromSeconds(egressData.HandlerLifetimeSeconds)); // Syncs factory management duration
    
    // Register the default object pool provider for making a pool of WebAckContext objects
    // Use a pooled policy for the MessageAckContext type
    builder.Services.AddSingleton<ObjectPoolProvider, DefaultObjectPoolProvider>();
    builder.Services.AddSingleton(sp =>
    {
        var provider = sp.GetRequiredService<ObjectPoolProvider>();
        return provider.Create<MessageAckContext>();
    });

    // Configure OpenTelemetry
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(appSettings.MetricsSettings.ServiceName))
        .WithMetrics(metrics =>
        {
            // Enable built-in .NET runtime metrics
            metrics.AddRuntimeInstrumentation();
            
            // ENABLE CUSTOM METRICS: Must match the string used in: new Meter("Damper.Core", "1.0.0");
            metrics.AddMeter(appSettings.MetricsSettings.MeterName);

            // 3. Export to OTLP (e.g., to an OTel Collector, Jaeger, or Honeycomb)
            metrics.AddOtlpExporter(options =>
            {
                // Set your collector endpoint (default is usually http://localhost:4317)
                options.Endpoint = new Uri(appSettings.MetricsSettings.OtlpEndpoint ?? DamperConstants.DAMPER_METER_OTLP_ENDPOINT);
                
                // If using HTTP instead of gRPC, set this:
                // options.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.HttpProtobuf;
            });
        });

    bootstrapLogger.Info($"BUILDING APPLICATION");
    var app = builder.Build();
    
    bootstrapLogger.Info($"Initializing loggers");
    var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
    Loggers.Initialize(loggerFactory);

    Loggers.Application.Info($"Setting middleware");
    app.UseMiddleware<CorrelationIdMiddleware>();
    
    // Configure the HTTP request pipeline.
    Loggers.Application.Info($"Configuring HTTP request pipeline");
    app.UseHttpsRedirection();
    
    Loggers.Application.Info($"Defining minimal API - MapPost");
    app.MapPost("v1/inbound", async (
        HttpContext context,
        IMessageIngestionService ingestionService,
        CancellationToken ct) =>
    {
        // Extract the API key from the HTTP headers
        if (!context.Request.Headers.TryGetValue(DamperConstants.REQUEST_X_DAMPER_API_KEY, out var apiKeyStrVal) ||
            StringValues.IsNullOrEmpty(apiKeyStrVal))
        {
            return TypedResults.Json(new { error = "Unable to authenticate." }, statusCode: StatusCodes.Status401Unauthorized);
        }
        var apiKey = apiKeyStrVal.ToString();
        
        // Middleware creates the correlation ID and puts it in the HttpContext.Items dictionary
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? $"SYSGEN-{CorrelationIdGenerator.Generate()}";
        var thisRequest = RequestWrapper.BuildFrom(new(correlationId), new(apiKey), context.Request.Headers, context.Request.Body, ct);
        var result = await ingestionService.ProcessIngressAsync(thisRequest);
        
        return result.IsSuccess
            ? Results.Accepted($"/v1/status/{result.Value}", new { trackingId = result.Value })
            : result.Error.Type switch
            {
                ErrorType.BadRequest  => Results.BadRequest(new { error = result.Error.Message }),
                ErrorType.NotFound    => Results.NotFound(new { error = result.Error.Message }),
                ErrorType.ServerError => TypedResults.Json(new { error = "An internal processing error occurred." }, 
                                                           statusCode: StatusCodes.Status500InternalServerError),
                ErrorType.Unauthorized => TypedResults.Json(new { error = "Unable to authenticate." }, 
                                                            statusCode: StatusCodes.Status401Unauthorized),
                _                     => TypedResults.Json(new { error = "Unknown error occurred" },
                                                           statusCode: StatusCodes.Status500InternalServerError)
            };
    });
    
    Loggers.Application.Info($"Calling app.Run");
    app.Run();
}
catch (Exception ex)
{
    bootstrapLogger.Fatal(ex, "Damper.io terminated unexpectedly");
    throw;
}
finally
{
    Loggers.Application.Info($"Shutting down NLog log manager");
    LogManager.Shutdown();
}
