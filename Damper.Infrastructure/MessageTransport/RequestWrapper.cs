using System.Net.Http.Headers;
using Damper.Domain.Common;
using Damper.Infrastructure.Logging;
using Damper.Infrastructure.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Damper.Infrastructure.MessageTransport
{
    public class RequestWrapper
    {
        private ApiKey _apiKey;
        public string ApiKeyMasked => _apiKey.Masked;
        public ApiKeyHash ApiKeyHash { get; set; }
        public CorrelationId CorrelationId{ get; set; } = new(string.Empty);
        public IHeaderDictionary HttpHeaders{ get; set; } = new HeaderDictionary();
        public Stream RequestBody{ get; set; } = new MemoryStream();
        public CancellationToken CancelToken { get; set; }
        public ErrorType ErrorType { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public static RequestWrapper BuildFrom(CorrelationId correlationId, ApiKey apiKey, IHeaderDictionary headers, Stream body, CancellationToken ct)
        {
            return new RequestWrapper
            {
                _apiKey = apiKey,
                ApiKeyHash = new(apiKey.ToHash()),
                CorrelationId = correlationId,
                HttpHeaders = headers,
                RequestBody = body,
                CancelToken = ct,
            };
        }

        public RequestWrapper SetError(string errorMessage, ErrorType errorType)
        {
            this.ErrorMessage = errorMessage;
            this.ErrorType = errorType;
            return this;
        }

        public bool IsProcessable()
        {
            return !(
                        string.IsNullOrWhiteSpace(CorrelationId.Value) ||
                        string.IsNullOrWhiteSpace(ApiKeyHash.ToString()) ||
                        HttpHeaders == null ||
                        HttpHeaders.Count == 0 ||
                        RequestBody == null
                    );
        }

        public async Task<ReadOnlyMemory<byte>> ReadRequestBodyToMemoryAsync()
        {
            // Use ArrayPool for high-performance, non-allocating byte storage
            var ms = new MemoryStream();
            await this.RequestBody.CopyToAsync(ms);
            return ms.GetBuffer().AsMemory(0, (int)ms.Length);
        }

        public Result<string> LogAndGenerateFailureResult()
        {
            Loggers.Request.Error($"{this.ErrorMessage}");
            return Result<string>.Failure(this.ErrorType, this.ErrorMessage);
        }

        public bool TryValidateContentType(out Result<string> result)
        {
            var contentTypeExists = this.HttpHeaders.TryGetValue("Content-Type", out StringValues contentTypeReceived);
            if (contentTypeExists)
            {
                Loggers.RequestTrace.Trace($"Request has Content-Type = {contentTypeReceived}");
                // Check for multiple Content-Type headers - a violation
                if (contentTypeReceived.Count > 1)
                {
                    result =  this.SetError($"The incoming message has multiple Content-Type header entries | HDR: {contentTypeReceived}", ErrorType.BadRequest)
                                .LogAndGenerateFailureResult();
                    return false;
                }
                if (!MediaTypeHeaderValue.TryParse(contentTypeReceived, out _))
                {
                    result = this.SetError($"The incoming message Content-Type header is unparsable | HDR: {contentTypeReceived}", ErrorType.BadRequest)
                            .LogAndGenerateFailureResult();
                    return false;
                }
            }
            result = Result<string>.Success(this.CorrelationId.Value);
            return true;
        }
    }
}