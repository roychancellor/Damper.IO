using Microsoft.AspNetCore.Http;
using Damper.Core.Models;

namespace Damper.Core.IngestionService;

public interface IWebhookIngestionService
{
    Task<Result<string>> ProcessIngressAsync(string customerId, IHeaderDictionary httpHeaders, Stream requestBody);
}
