using Damper.Domain.Common;

namespace Damper.Infrastructure.Security
{
    public interface ISecretProtector
    {
        ProtectedSecret Protect(Secret secret);

        Secret Unprotect(ProtectedSecret protectedSecret);
    }
}