using System.Security.Cryptography;
using System.Text;
using Damper.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Damper.Infrastructure.QueueManagement;
using Damper.Core.Models;
using Damper.Infrastructure.Logging;

namespace Damper.Core.IngestionService;

public class WebhookIngestionService : IWebhookIngestionService
{
    private static readonly ILogger _log = Loggers.Request;
    
    private readonly ICustomerRepository _customerRepo;
    private readonly IQueuePublisher _queuePublisher;

    public WebhookIngestionService(ICustomerRepository tenantRepo, IQueuePublisher queuePublisher)
    {
        _customerRepo = tenantRepo;
        _queuePublisher = queuePublisher;
    }

    public async Task<Result<string>> ProcessIngressAsync(string correlationId, string customerId, IHeaderDictionary httpHeaders, Stream requestBody)
    {
        string msg;
        
        _log.Info($"====> New webhook request received | CUSTOMER: {customerId}");
        var customerConfig = await _customerRepo.GetByIdAsync(customerId);
        if (customerConfig == null)
        {
            msg = $"Customer configuration for '{customerId}' is missing or corrupted.";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.ServerError, msg);
        }

        string? incomingSignature = httpHeaders[customerConfig.WebhookHeaderKey];
        if (string.IsNullOrEmpty(incomingSignature))
        {
            msg = "The incoming webhook header key cannot be null or empty";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.BadRequest, msg);
        }
        
        using var reader = new StreamReader(requestBody);
        string rawPayload = await reader.ReadToEndAsync();
        if (string.IsNullOrEmpty(rawPayload))
        {
            msg = "The incoming webhook payload cannot be null or empty";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.BadRequest, msg);
        }

        if (!VerifyHmacSignature(rawPayload, incomingSignature, customerConfig.SecretKey))
        {
            msg = "The incoming webhook payload hash does not match the incoming signature";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.BadRequest, msg);
        }

        var toPublishStr = WebhookEnvelope.BuildToPublish(correlationId, customerId, customerConfig.DestinationURL, rawPayload)
                                          .Jsonify();
        if (string.IsNullOrEmpty(toPublishStr))
        {
            msg = "Unable to serialize the ingested webhook payload";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.BadRequest, msg);
        }

        var isPublishSuccessful = await _queuePublisher.PublishAsync(customerId, toPublishStr);
        if (!isPublishSuccessful)
        {
            msg = "Unable to publish ingested webhook payload to message broker";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.ServerError, msg);
        }

        // Success! Return a tracking ID back to the API
        _log.Info($"<==== Webhook request processed | CUSTOMER: {customerId}");
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