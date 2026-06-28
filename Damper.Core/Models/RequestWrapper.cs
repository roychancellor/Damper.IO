using Microsoft.AspNetCore.Http;

namespace Damper.Core.Models
{
    public class RequestWrapper
    {
        public string CorrelationId{ get; set; } = string.Empty;
        public string CustomerId{ get; set; } = string.Empty;
        public IHeaderDictionary HttpHeaders{ get; set; } = new HeaderDictionary();
        public Stream RequestBody{ get; set; } = new MemoryStream();
        public CancellationToken CancelToken { get; set; }
        public ErrorType ErrorType { get; set; }
        public string ErrorMessage { get; set; } = string.Empty;

        public static RequestWrapper BuildFrom(string correlationId, string customerId, IHeaderDictionary headers, Stream body, CancellationToken ct)
        {
            return new RequestWrapper
            {
                CorrelationId = correlationId,
                CustomerId = customerId,
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
                        string.IsNullOrWhiteSpace(CorrelationId) ||
                        string.IsNullOrWhiteSpace(CustomerId) ||
                        HttpHeaders == null ||
                        HttpHeaders.Count == 0 ||
                        RequestBody == null
                    );
        }
    }
}