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
        var customerId = rw.CustomerId;
        var correlationId = rw.CorrelationId;

        _log.Info($"====> New webhook request received | CUSTOMER: {customerId} | CORRELATION: {correlationId}");
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
        
        // TODO: Investigate streaming the webhook payload to the queue rather than reading
        // the entire payload into memory. If configured correctly, the upstream reverse proxy
        // e.g., HAProxy will reject payloads above a certain size, so streaming my not be
        // needed.
        using var reader = new StreamReader(rw.RequestBody ?? throw new ArgumentNullException(nameof(rw), $"Request body stream cannot be null"));
        string rawPayload = await reader.ReadToEndAsync(rw.CancelToken);
        if (string.IsNullOrEmpty(rawPayload))
        {
            return LogAndGenerateFailureResult(rw.SetError("The incoming webhook payload cannot be null or empty", ErrorType.BadRequest));
        }

        var toPublishStr = WebhookEnvelope.BuildToPublish(correlationId, customerId, customerConfig.DestinationURL, rawPayload)
                                          .Jsonify();
        if (string.IsNullOrEmpty(toPublishStr))
        {
            return LogAndGenerateFailureResult(rw.SetError("Unable to serialize the ingested webhook payload", ErrorType.BadRequest));
        }

        // By business decision, we are passing a combined token that will prevent publishing if the HTTP request
        // is canceled OR if the application shuts down. In either case, the webhook providers will not receive
        // a success status code and will retry.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(rw.CancelToken, _appLifetime.ApplicationStopping);
        try
        {
            var pw = PublishWrapper.BuildFrom(correlationId, customerId, toPublishStr, linkedCts.Token, shouldThrow: false);
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
        _log.Info($"<==== Webhook request processed | CUSTOMER: {customerId} | CORRELATION: {correlationId}");
        return Result<string>.Success(correlationId);
    }

    private Result<string> LogAndGenerateFailureResult(RequestWrapper rw)
    {
        _log.Error($"{rw.ErrorMessage} | CUSTOMER: {rw.CustomerId} | CORRELATION: {rw.CorrelationId}");
        return Result<string>.Failure(rw.ErrorType, rw.ErrorMessage);
    }
}