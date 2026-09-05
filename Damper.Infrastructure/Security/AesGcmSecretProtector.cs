using Damper.Domain.Common;
using Damper.Infrastructure.ReferenceData;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace Damper.Infrastructure.Security;

public sealed class AesGcmSecretProtector : ISecretProtector
{
    private const int KeyLength = 32;
    private const int NonceLength = 12;
    private const int TagLength = 16;

    private readonly byte[] _key;
    private readonly int _keyVersion;

    public AesGcmSecretProtector(IOptions<EncryptionSettings> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var settings = options.Value;

        if (string.IsNullOrWhiteSpace(settings.Key))
        {
            throw new InvalidOperationException("Encryption key is not configured.");
        }

        try
        {
            _key = Convert.FromBase64String(settings.Key);
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("Encryption key must be a valid Base64 value.", ex);
        }

        if (_key.Length != KeyLength)
        {
            throw new InvalidOperationException($"Encryption key must decode to exactly {KeyLength} bytes.");
        }

        if (settings.KeyVersion <= 0)
        {
            throw new InvalidOperationException("Encryption key version must be greater than zero.");
        }

        _keyVersion = settings.KeyVersion;
    }

    public ProtectedSecret Protect(Secret secret)
    {
        var plaintext = Encoding.UTF8.GetBytes(secret.Reveal());

        var nonce = RandomNumberGenerator.GetBytes(NonceLength);
        var ciphertext = new byte[plaintext.Length];
        var tag = new byte[TagLength];

        using var aes = new AesGcm(_key, TagLength);

        aes.Encrypt(nonce, plaintext, ciphertext, tag);

        CryptographicOperations.ZeroMemory(plaintext);

        return new ProtectedSecret(ciphertext, nonce, tag, _keyVersion);
    }

    public Secret Unprotect(ProtectedSecret protectedSecret)
    {
        ArgumentNullException.ThrowIfNull(protectedSecret);

        if (protectedSecret.KeyVersion != _keyVersion)
        {
            throw new InvalidOperationException($"Encryption key version {protectedSecret.KeyVersion} is not available.");
        }

        var plaintext = new byte[protectedSecret.Ciphertext.Length];

        try
        {
            using var aes = new AesGcm(_key, TagLength);

            aes.Decrypt(
                protectedSecret.Nonce,
                protectedSecret.Ciphertext,
                protectedSecret.Tag,
                plaintext);

            return new Secret(Encoding.UTF8.GetString(plaintext));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }
}