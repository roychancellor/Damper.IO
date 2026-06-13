using System.Security.Cryptography;
using System.Text;
using Damper.Infrastructure.Repositories;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Damper.Infrastructure.QueueManagement;
using Damper.Core.Models;
using Damper.Infrastructure.Logging;

namespace Damper.Core.IngestionService;

public class WebhookIngestionService : IWebhookIngestionService
{
    private static readonly ILogger _log = Loggers.Request;
    private readonly IHostApplicationLifetime _appLifetime;
    
    private readonly ICustomerRepository _customerRepo;
    private readonly IQueuePublisher _queuePublisher;

    public WebhookIngestionService(ICustomerRepository tenantRepo, IQueuePublisher queuePublisher, IHostApplicationLifetime appLifetime)
    {
        _customerRepo = tenantRepo;
        _queuePublisher = queuePublisher;
        _appLifetime = appLifetime;
    }

    public async Task<Result<string>> ProcessIngressAsync(string correlationId,
                                                          string customerId,
                                                          IHeaderDictionary httpHeaders,
                                                          Stream requestBody,
                                                          CancellationToken ct)
    {
        _log.Info($"====> New webhook request received | CUSTOMER: {customerId}");
        var customerConfig = await _customerRepo.GetByIdAsync(customerId, ct);
        if (customerConfig == null)
        {
            return LogAndGenerateFailureResult(customerId, $"Customer configuration for '{customerId}' is missing or corrupted.", ErrorType.ServerError);
        }

        string? incomingSignature = httpHeaders[customerConfig.WebhookHeaderKey];
        if (string.IsNullOrEmpty(incomingSignature))
        {
            return LogAndGenerateFailureResult(customerId, "The incoming webhook header key cannot be null or empty", ErrorType.BadRequest);
        }
        
        using var reader = new StreamReader(requestBody);
        string rawPayload = await reader.ReadToEndAsync(ct);
        if (string.IsNullOrEmpty(rawPayload))
        {
            return LogAndGenerateFailureResult(customerId, "The incoming webhook payload cannot be null or empty", ErrorType.BadRequest);
        }

        if (!VerifyHmacSignature(rawPayload, incomingSignature, customerConfig.SecretKey))
        {
            return LogAndGenerateFailureResult(customerId, "The incoming webhook payload hash does not match the incoming signature", ErrorType.BadRequest);
        }

        var toPublishStr = WebhookEnvelope.BuildToPublish(correlationId, customerId, customerConfig.DestinationURL, rawPayload)
                                          .Jsonify();
        if (string.IsNullOrEmpty(toPublishStr))
        {
            return LogAndGenerateFailureResult(customerId, "Unable to serialize the ingested webhook payload", ErrorType.BadRequest);
        }

        // By business decision, we are passing a combined token that will prevent publishing if the HTTP request
        // is canceled OR if the application shuts down. In either case, the webhook providers will not receive
        // a success status code and will retry.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, _appLifetime.ApplicationStopping);
        try
        {
            var isPublishSuccessful = await _queuePublisher.PublishAsync(customerId, toPublishStr, linkedCts.Token);
            if (!isPublishSuccessful)
            {
                return LogAndGenerateFailureResult(customerId, "Unable to publish ingested webhook payload to message broker", ErrorType.ServerError);
            }
        }
        catch (OperationCanceledException) when (_appLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _log.Warn($"Publish aborted for customer {customerId} due to application shutdown sequence.");
            throw;
        }

        // Success! Return a tracking ID back to the API
        _log.Info($"<==== Webhook request processed | CUSTOMER: {customerId}");
        return Result<string>.Success(correlationId);
    }

    private Result<string> LogAndGenerateFailureResult(string customerId, string msg, ErrorType errorType)
    {
        _log.Error(msg);
        return Result<string>.Failure(errorType, msg);
    }

    private static bool VerifyHmacSignature(string payload, string incomingSignature, string secretKey)
    {
        // Defend against null or obviously malformed signatures immediately
        if (string.IsNullOrEmpty(incomingSignature) || incomingSignature.Length != 64)
        {
            return false;
        }

        // Convert incoming hex string straight to bytes without string allocations
        byte[] incomingBytes;
        try
        {
            incomingBytes = Convert.FromHexString(incomingSignature);
        }
        catch (FormatException)
        {
            return false; // Not a valid hex string
        }

        // Compute the native hash
        var keyBytes = Encoding.UTF8.GetBytes(secretKey);
        var payloadBytes = Encoding.UTF8.GetBytes(payload);

        using var hmac = new HMACSHA256(keyBytes);
        var computedHashBytes = hmac.ComputeHash(payloadBytes);

        // Compare the two native byte arrays directly in constant time
        return CryptographicOperations.FixedTimeEquals(computedHashBytes, incomingBytes);
    }
}