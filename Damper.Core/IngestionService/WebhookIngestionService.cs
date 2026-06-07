using System.Security.Cryptography;
using System.Text;
using Damper.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using RabbitMQ.Client;

namespace Damper.Core.IngestionService;

public class WebhookIngestionService : IWebhookIngestionService
{
    private readonly ICustomerRepository _customerRepo;
    private readonly IChannel _rabbitChannel;

    public WebhookIngestionService(ICustomerRepository tenantRepo, IChannel rabbitChannel)
    {
        _customerRepo = tenantRepo;
        _rabbitChannel = rabbitChannel;
    }

    public async Task<bool> ProcessIngressAsync(string customerId, IHeaderDictionary httpHeaders, Stream requestBody)
    {
        var customerConfig = await _customerRepo.GetByIdAsync(customerId);
        if (customerConfig == null) return false;

        string? incomingSignature = httpHeaders[customerConfig.WebhookHeaderKey];
        if (string.IsNullOrEmpty(incomingSignature)) return false;
        
        using var reader = new StreamReader(requestBody);
        string rawPayload = await reader.ReadToEndAsync();
        if (string.IsNullOrEmpty(rawPayload)) return false;

        if (!VerifyHmacSignature(rawPayload, incomingSignature, customerConfig.SecretKey))
        {
            return false;
        }

        // Push straight to the RabbitMQ Shard/Exchange
        var bodyBytes = Encoding.UTF8.GetBytes(rawPayload);
        
        // Modern v7+ Properties Setup: Flawless async delivery tracking
        var properties = new BasicProperties
        {
            DeliveryMode = DeliveryModes.Persistent, // Equivalent to old Persistent = true
            Headers = new Dictionary<string, object?>
            {
                { "CustomerId", customerId }
            }
        };

        // Modern v7+ async publishing pattern
        await _rabbitChannel.BasicPublishAsync(
            exchange: "damper.webhook.exchange",
            routingKey: $"webhook.shard.{customerId}",
            mandatory: true,
            basicProperties: properties,
            body: bodyBytes
        );

        return true;
    }

    private static bool VerifyHmacSignature(string payload, string incomingSignature, string secretKey)
    {
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var computedHashBytes = hmac.ComputeHash(payloadBytes);
        var computedSignature = Convert.ToHexString(computedHashBytes).ToLowerInvariant();

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedSignature), 
            Encoding.UTF8.GetBytes(incomingSignature)
        );
    }
}