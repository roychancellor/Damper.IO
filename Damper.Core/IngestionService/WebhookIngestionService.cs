using Damper.Infrastructure.Repositories;
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

    public async Task<Result<string>> ProcessIngressAsync(RequestWrapper rw)
    {
        if (rw == null || !rw.IsProcessable())
        {
            var msg = $"The incoming webhook request is null or unprocessable";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.ServerError, msg);
        }
        var customerId = rw.CustomerId;
        var correlationId = rw.CorrelationId;

        _log.Info($"====> New webhook request received | CUSTOMER: {customerId}");
        var customerConfig = await _customerRepo.GetByIdAsync(customerId, rw.CancelToken);
        if (customerConfig == null)
        {
            return LogAndGenerateFailureResult(rw.SetError($"Customer configuration is missing or corrupted", ErrorType.ServerError));
        }

        // TODO: Decide whether to keep this or delete it. Customers would need to specify
        // the name of the webhook signature header. Checking its presence could offer a
        // small amount of extra security by bouncing very obviously invalid requests.
        string? incomingSignature = rw.HttpHeaders[customerConfig.WebhookHeaderKey];
        if (string.IsNullOrEmpty(incomingSignature))
        {
            return LogAndGenerateFailureResult(rw.SetError("The incoming webhook header key cannot be null or empty", ErrorType.BadRequest));
        }

        // To preserve the webhook payload byte-for-byte, convert it to a byte array, then base 64 encode the array to a string
        var base64Body = await StreamToBase64String(rw);
        if (string.IsNullOrEmpty(base64Body))
        {
            return LogAndGenerateFailureResult(rw.SetError("The incoming webhook payload cannot be null or empty", ErrorType.BadRequest));
        }

        var headerDictionary = rw.HttpHeaders.ToDictionary(h => h.Key, h => h.Value.ToString());

        var toPublishEnvelope = WebhookEnvelope.BuildBase(rw)
                                               .SetDestination(customerConfig.DestinationURL)
                                               .SetPayload(base64Body)
                                               .SetHeaders(headerDictionary)
                                               .Jsonify();
        if (string.IsNullOrEmpty(toPublishEnvelope))
        {
            return LogAndGenerateFailureResult(rw.SetError("Unable to serialize the ingested webhook payload", ErrorType.BadRequest));
        }

        // By business decision, we are passing a combined token that will prevent publishing if the HTTP request
        // is canceled OR if the application shuts down. In either case, the webhook providers will not receive
        // a success status code and will retry.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(rw.CancelToken, _appLifetime.ApplicationStopping);
        try
        {
            var pw = PublishWrapper.BuildBase(linkedCts.Token, shouldThrow: true)
                                   .SetCorrelationID(correlationId)
                                   .SetCustomerID(customerId)
                                   .SetPayload(toPublishEnvelope);
            var isPublishSuccessful = await _queuePublisher.PublishAsync(pw);
            if (!isPublishSuccessful)
            {
                return LogAndGenerateFailureResult(rw.SetError("Unable to publish ingested webhook payload to message broker", ErrorType.ServerError));
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

    private static async Task<string> StreamToBase64String(RequestWrapper rw)
    {
        using var memoryStream = new MemoryStream();
        await rw.RequestBody.CopyToAsync(memoryStream);
        var rawBodyBytes = memoryStream.ToArray();
        var base64Body = Convert.ToBase64String(rawBodyBytes);
        return base64Body;
    }

    private static Result<string> LogAndGenerateFailureResult(RequestWrapper rw)
    {
        _log.Error($"{rw.ErrorMessage} | CUSTOMER: {rw.CustomerId}");
        return Result<string>.Failure(rw.ErrorType, rw.ErrorMessage);
    }
}