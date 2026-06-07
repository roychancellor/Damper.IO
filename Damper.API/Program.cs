using Damper.Core.IngestionService;
using Damper.Infrastructure.Extensions;
using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.Caching.Memory;
using RabbitMQ.Client;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
//builder.Services.AddOpenApi();

builder.Services.AddRepositories(builder.Configuration);
builder.Services.AddScoped<IWebhookIngestionService, WebhookIngestionService>();
await builder.Services.AddRabbitMqInfrastructureAsync(builder.Configuration);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    //app.MapOpenApi();
}

app.UseHttpsRedirection();

app.MapPost("v1/inbound/{customerId}", async (
    string customerId, 
    HttpRequest request, 
    IWebhookIngestionService ingestionService) =>
{
    var result = await ingestionService.ProcessIngressAsync(customerId, request.Headers, request.Body);
    
    return result ? Results.Accepted() : Results.BadRequest("Invalid payload or customer configuration");
});

app.Run();
