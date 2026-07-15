using Microsoft.AspNetCore.Http;
using Damper.Infrastructure.Models;

namespace Damper.Core.IngestionService;

public interface IWebhookIngestionService
{
    Task<Result<string>> ProcessIngressAsync(RequestWrapper requestWrapper);
}
