using Microsoft.AspNetCore.Http;
using Damper.Infrastructure.MessageTransport;

namespace Damper.Core.IngestionService;

public interface IMessageIngestionService
{
    Task<Result<string>> ProcessIngressAsync(RequestWrapper requestWrapper);
}
