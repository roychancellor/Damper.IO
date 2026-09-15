using System.Security.Cryptography;
using System.Text;
using Damper.Domain.Common;

namespace Damper.Infrastructure.Security;

public static class ApiKeyExtensions
{
    public static ApiKeyHash ToHash(this ApiKey apiKey)
    {
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey.Reveal()));

        return new ApiKeyHash(hashBytes);
    }
}