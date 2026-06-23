using Damper.Core.IngestionService;
using Damper.Core.Middleware;
using Damper.Core.Models;
using Damper.Core.Utilities;
using Damper.Infrastructure.Extensions;
using Damper.Infrastructure.Logging;
using NLog.Web;
using NLog;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Options;

var bootstrapLogger = LogManager.Setup().GetCurrentClassLogger();

try
{
    bootstrapLogger.Info($"DAMPER.IO APPLICATION STARTING");

    bootstrapLogger.Info($"Creating web application builder");
    var builder = WebApplication.CreateBuilder(args);

    bootstrapLogger.Info($"Configuring application data");
    builder.Services.Configure<AppRefData>(builder.Configuration.GetSection("ApplicationData"));
    
    bootstrapLogger.Info($"Setting NLog as the logging provider");
    builder.Logging.ClearProviders();
    builder.Host.UseNLog();
    
    bootstrapLogger.Info($"Adding services");
    builder.Services.AddRepositories()
                    .AddRabbitMqInfrastructure()
                    .AddQueuePublishing()
                    .AddWebhookIngestion();
    
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
    app.MapPost("v1/inbound/{customerId}", async (
        string customerId, 
        HttpContext context,
        IWebhookIngestionService ingestionService,
        CancellationToken ct) =>
    {
        // Middleware creates the correlation ID and puts it in the HttpContext.Items dictionary
        var correlationId = context.Items["CorrelationId"]?.ToString() ?? $"SYSGEN-{CorrelationIdGenerator.Generate()}";
        var thisRequest = RequestWrapper.BuildFrom(correlationId, customerId, context.Request.Headers, context.Request.Body, ct);
        var result = await ingestionService.ProcessIngressAsync(thisRequest);
        
        return result.IsSuccess
            ? Results.Accepted($"/v1/status/{result.Value}", new { trackingId = result.Value })
            : result.Error.Type switch
            {
                ErrorType.BadRequest  => Results.BadRequest(new { error = result.Error.Message }),
                ErrorType.NotFound    => Results.NotFound(new { error = result.Error.Message }),
                ErrorType.ServerError => TypedResults.Json(new { error = "An internal processing error occurred." }, 
                                                           statusCode: StatusCodes.Status500InternalServerError),
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
