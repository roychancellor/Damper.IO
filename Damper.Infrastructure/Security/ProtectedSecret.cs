namespace Damper.Infrastructure.Security
{
    public sealed record ProtectedSecret(
    byte[] Ciphertext,
    byte[] Nonce,
    byte[] Tag,
    int KeyVersion);
}