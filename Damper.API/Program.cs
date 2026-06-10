using Damper.Core.IngestionService;
using Damper.Core.Models;
using Damper.Infrastructure.Extensions;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRepositories(builder.Configuration);
builder.Services.AddScoped<IWebhookIngestionService, WebhookIngestionService>();
await builder.Services.AddRabbitMqInfrastructureAsync(builder.Configuration);
builder.Services.AddQueuePublishing();

var app = builder.Build();

// Configure the HTTP request pipeline.
app.UseHttpsRedirection();

app.MapPost("v1/inbound/{customerId}", async (
    string customerId, 
    HttpRequest request, 
    IWebhookIngestionService ingestionService) =>
{
    var result = await ingestionService.ProcessIngressAsync(customerId, request.Headers, request.Body);
    
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

app.Run();
