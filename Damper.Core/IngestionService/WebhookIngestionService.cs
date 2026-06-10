using System.Security.Cryptography;
using System.Text;
using Damper.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
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

    public async Task<Result<string>> ProcessIngressAsync(string customerId, IHeaderDictionary httpHeaders, Stream requestBody)
    {
        var customerConfig = await _customerRepo.GetByIdAsync(customerId);
        if (customerConfig == null)
        {
            return Result<string>.Failure(ErrorType.ServerError, $"Customer configuration for '{customerId}' is missing or corrupted.");
        }

        string? incomingSignature = httpHeaders[customerConfig.WebhookHeaderKey];
        if (string.IsNullOrEmpty(incomingSignature))
        {
            return Result<string>.Failure(ErrorType.BadRequest, "The incoming webhook header key cannot be null or empty");
        }
        
        using var reader = new StreamReader(requestBody);
        string rawPayload = await reader.ReadToEndAsync();
        if (string.IsNullOrEmpty(rawPayload))
        {
            return Result<string>.Failure(ErrorType.BadRequest, "The incoming webhook payload cannot be null or empty");
        }

        if (!VerifyHmacSignature(rawPayload, incomingSignature, customerConfig.SecretKey))
        {
            return Result<string>.Failure(ErrorType.BadRequest, "The incoming webhook payload hash does not match the incoming signature");
        }

        var toPublishStr = WebhookEnvelope.BuildToPublish(customerId, customerConfig.DestinationURL, rawPayload)
                                          .Jsonify();
        if (string.IsNullOrEmpty(toPublishStr))
        {
            return Result<string>.Failure(ErrorType.BadRequest, "Unable to serialize the ingested webhook payload");
        }

        var isPublishSuccessful = await _queuePublisher.PublishAsync(customerId, toPublishStr);
        if (!isPublishSuccessful)
        {
            return Result<string>.Failure(ErrorType.ServerError, "Unable to publish ingested webhook payload to message broker");
        }

        // Success! Return a tracking ID back to the API
        string trackingId = Guid.NewGuid().ToString("N");
        return Result<string>.Success(trackingId);
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