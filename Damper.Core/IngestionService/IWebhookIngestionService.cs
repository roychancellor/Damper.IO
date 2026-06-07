using Microsoft.AspNetCore.Http;

namespace Damper.Core.IngestionService;

public interface IWebhookIngestionService
{
    Task<bool> ProcessIngressAsync(string customerId, IHeaderDictionary httpHeaders, Stream requestBody);
}
