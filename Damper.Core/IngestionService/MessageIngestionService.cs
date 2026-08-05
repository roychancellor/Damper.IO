using Damper.Infrastructure.Repositories;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Damper.Infrastructure.QueueManagement;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.MessageTransport;
using Damper.Domain.Common;
using Damper.Infrastructure.Security;

namespace Damper.Core.IngestionService;

public class MessageIngestionService : IMessageIngestionService
{
    private static readonly ILogger _log = Loggers.Request;
    private static readonly ILogger _traceLog = Loggers.RequestTrace;

    private readonly IHostApplicationLifetime _appLifetime;
    
    private readonly IIntegrationRepository _integRepo;
    private readonly IQueuePublisher _queuePublisher;

    public MessageIngestionService(IIntegrationRepository integRepo, IQueuePublisher queuePublisher, IHostApplicationLifetime appLifetime)
    {
        _integRepo = integRepo;
        _queuePublisher = queuePublisher;
        _appLifetime = appLifetime;
    }

    public async Task<Result<string>> ProcessIngressAsync(RequestWrapper rw)
    {
        _traceLog.Trace($"====> ProcessIngressAsync STARTING");

        if (rw == null || !rw.IsProcessable())
        {
            var msg = $"The incoming message request is null or unprocessable";
            _log.Error(msg);
            return Result<string>.Failure(ErrorType.ServerError, msg);
        }
        var apiKeyHash = rw.ApiKeyHash;
        var corrId = rw.CorrelationId;

        _log.Info($"====> New message request received | CORRELATION ID: {corrId}");
        _traceLog.Trace($"Getting integration from repo by API KEY (REDACTED)");
        var integration = await _integRepo.GetByApiKeyHashAsync(apiKeyHash, rw.CancelToken);
        if (integration == null)
        {
            return rw.SetError($"ApiKey not found - treat as unauthorized | API KEY (MASKED): {rw.ApiKeyMasked}", ErrorType.Unauthorized).LogAndGenerateFailureResult();
        }
        _traceLog.Trace($"Integration retrieved | INTEG ID: {integration.Id} | NAME: {integration.Name}");

        // Verify the Content-Type header is parsable as a known type, as the dispatcher needs it to be correct
        // to send a valid request to the destination. Checking here allows for HTTP 400 if it is not parsable.
        if (!rw.TryValidateContentType(out Result<string> badRequestResult))
        {
            return badRequestResult;
        }
        
        // Preserve the message payload byte-for-byte (payload agnostic)
        _traceLog.Trace($"Reading request body to bytes");
        var rawBodyBytes = await rw.ReadRequestBodyToMemoryAsync();
        if (rawBodyBytes.IsEmpty)
        {
            return rw.SetError("The incoming message payload cannot be null or empty", ErrorType.BadRequest).LogAndGenerateFailureResult();
        }
        _traceLog.Trace($"Read request body successfully | NUM BYTES: {rawBodyBytes.Length}");

        var httpHeaderDictionary = rw.HttpHeaders.ToDictionary(h => h.Key, h => h.Value.ToString());

        // By business decision, we are passing a combined token that will prevent publishing if the ingress HTTP request
        // is canceled OR if the application shuts down. In either case, the message providers will not receive
        // a success status code and will retry.
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(rw.CancelToken, _appLifetime.ApplicationStopping);

        // Mental model: The actual payload published to the queue is the bytes of the RawBodyBytes
        // of the MessageEnvelope. The rest is metadata placed in message headers for transport.
        // This eliminates the need for serialization/deserialization to preserve the original
        // payload byte-for-byte.
        _traceLog.Trace($"Building Message Envelope");
        var toPublishEnvelope = MessageEnvelope
                                .BuildBase(rw, shouldThrow: true, linkedCts.Token)
                                .SetDestination(integration.Delivery.Destination.Uri.OriginalString)
                                .SetPayload(rawBodyBytes)
                                .SetHeaders(httpHeaderDictionary)
                                .SetIntegrationId(integration.Id)
                                .SetIntegrationName(integration.Name.Value);
        if (toPublishEnvelope == null || toPublishEnvelope.RawPayloadBytes.IsEmpty)
        {
            return rw.SetError("Message Envelope to publish is null or empty of content", ErrorType.BadRequest).LogAndGenerateFailureResult();
        }

        try
        {
            _traceLog.Trace($"Publishing to message exchange");
            if (!await _queuePublisher.TryPublishAsync(toPublishEnvelope))
            {
                return rw.SetError("Unable to publish ingested message payload to message exchange", ErrorType.ServerError).LogAndGenerateFailureResult();
            }
        }
        catch (OperationCanceledException) when (_appLifetime.ApplicationStopping.IsCancellationRequested)
        {
            _log.Warn($"Publish aborted for correlation ID {corrId.Value} due to application shutdown sequence.");
            throw;
        }

        // Success! Return a tracking ID (correlation ID) back to the API
        _traceLog.Trace($"<==== ProcessIngressAsync FINISHED | CORRELATION ID: {corrId.Value} | INTEG ID: {integration.Id} | NAME: {integration.Name}");
        _log.Info($"<==== Message request ingested and published | CORRELATION ID: {corrId.Value} | INTEG ID: {integration.Id} | NAME: {integration.Name}");
        return Result<string>.Success(corrId.Value);
    }
}