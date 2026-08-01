using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Damper.Infrastructure.QueueManagement;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.MessageTransport;

namespace Damper.Core.IngestionService;

public class MessageIngestionService : IMessageIngestionService
{
    private static readonly ILogger _log = Loggers.Request;
    private static readonly ILogger _traceLog = Loggers.RequestTrace;

    private readonly IHostApplicationLifetime _appLifetime;
    
    private readonly IIntegrationRepository _customerRepo;
    private readonly IQueuePublisher _queuePublisher;

    public MessageIngestionService(IIntegrationRepository tenantRepo, IQueuePublisher queuePublisher, IHostApplicationLifetime appLifetime)
    {
        _customerRepo = tenantRepo;
        _queuePublisher = queuePublisher;
        _appLifetime = appLifetime;
    }

    public async Task<Result<string>> ProcessIngressAsync(RequestWrapper rw)
    {
        _traceLog.Trace($"====> ProcessIngressAsync STARTING");

        if (rw == null || !rw.IsProcessable())
        {
            var msg = $"The incoming webhook request is null or unprocessable";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.ServerError, msg);
        }
        var customerId = rw.CustomerId;
        var correlationId = rw.CorrelationId;

        _log.Info($"====> New webhook request received | CUSTOMER: {customerId}");
        _traceLog.Trace($"Getting customer config from repo | CUST ID: {customerId}");
        var customerConfig = await _customerRepo.GetByIdAsync(customerId, rw.CancelToken);
        if (customerConfig == null)
        {
            return rw.SetError($"Customer configuration is missing or corrupted", ErrorType.ServerError).LogAndGenerateFailureResult();
        }

        // Verify the Content-Type header is parsable as a known type, as the dispatcher needs it to be correct
        // to send a valid request to the customer. Checking here allows for HTTP 400 if it is not parsable.
        if (!rw.TryValidateContentType(out Result<string> badRequestResult))
        {
            return badRequestResult;
        }
        
        // Preserve the webhook payload byte-for-byte (payload agnostic)
        _traceLog.Trace($"Reading request body to bytes");
        var rawBodyBytes = await rw.ReadRequestBodyToMemoryAsync();
        if (rawBodyBytes.IsEmpty)
        {
            return rw.SetError("The incoming webhook payload cannot be null or empty", ErrorType.BadRequest).LogAndGenerateFailureResult();
        }
        _traceLog.Trace($"Read request body successfully | NUM BYTES: {rawBodyBytes.Length}");

        var httpHeaderDictionary = rw.HttpHeaders.ToDictionary(h => h.Key, h => h.Value.ToString());

        _traceLog.Trace($"Building Webhook Envelope");
        var toPublishEnvelope = MessageEnvelope
                                .BuildBase(rw)
                                .SetDestination(customerConfig.DestinationURL)
                                .SetPayload(rawBodyBytes)
                                .SetHeaders(httpHeaderDictionary);
        if (toPublishEnvelope == null || toPublishEnvelope.RawPayloadBytes.IsEmpty)
        {
            return rw.SetError("Webhook Envelope to publish is null or empty of content", ErrorType.BadRequest).LogAndGenerateFailureResult();
        }

        // By business decision, we are passing a combined token that will prevent publishing if the HTTP request
        // is canceled OR if the application shuts down. In either case, the webhook providers will not receive
        // a success status code and will retry.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(rw.CancelToken, _appLifetime.ApplicationStopping);
        try
        {
            _traceLog.Trace($"Building Publish Wrapper and publishing to queue");
            var pw = PublishWrapper
                     .BuildBase(linkedCts.Token, shouldThrow: true)
                     .SetCorrelationID(correlationId)
                     .SetCustomerID(customerId)
                     .SetPayload(toPublishEnvelope);
            
            if (!await _queuePublisher.TryPublishAsync(pw))
            {
                return rw.SetError("Unable to publish ingested webhook payload to message broker", ErrorType.ServerError).LogAndGenerateFailureResult();
            }
        }
        catch (OperationCanceledException) when (_appLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _log.Warn($"Publish aborted for customer {customerId} due to application shutdown sequence.");
            throw;
        }

        // Success! Return a tracking ID (correlation ID) back to the API
        _traceLog.Trace($"<==== ProcessIngressAsync FINISHED | CUSTOMER: {customerId}");
        _log.Info($"<==== Webhook request ingested and published | CUSTOMER: {customerId}");
        return Result<string>.Success(correlationId);
    }
}