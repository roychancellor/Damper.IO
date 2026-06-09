using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Damper.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using RabbitMQ.Client;
using Damper.Infrastructure.QueueManagement;
using Damper.Core.Models;

namespace Damper.Core.IngestionService;

public class WebhookIngestionService : IWebhookIngestionService
{
    private readonly ICustomerRepository _customerRepo;
    private readonly IQueuePublisher _queuePublisher;

    public WebhookIngestionService(ICustomerRepository tenantRepo, IQueuePublisher queuePublisher)
    {
        _customerRepo = tenantRepo;
        _queuePublisher = queuePublisher;
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

        var toPublish = WebhookEnvelope.BuildToPublish(customerId, customerConfig.DestinationURL, rawPayload);
        var toPublishStr = JsonSerializer.Serialize(toPublish);

        var isPublishSuccessful = await _queuePublisher.PublishAsync(customerId, toPublishStr);
        return isPublishSuccessful;
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