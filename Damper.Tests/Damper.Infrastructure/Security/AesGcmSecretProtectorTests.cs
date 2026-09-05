using Damper.Domain.Common;
using Damper.Infrastructure.ReferenceData;
using Damper.Infrastructure.Security;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Damper.Tests;

/*
 * encrypt/decrypt round trip
same plaintext produces different ciphertext because nonce changes
bad key length fails startup/construction
tampered ciphertext/tag fails decryption
 */

[TestClass]
public class AesGcmSecretProtectorTests
{
    private const string TEST_KEY = "ABC123";
    
    [TestMethod]
    public void Protect_Unprotect_RoundTrip_ShouldPassIf_ReturnedValueMatches()
    {
        var ut = AesGcmSecretProtectorTestSetup.CreateProtector();

        var secretUT = new Secret(TEST_KEY);

        var protectResult = ut.Protect(secretUT);

        Assert.IsNotNull(protectResult);
        Assert.IsNotEmpty(protectResult.Ciphertext);
        Assert.IsNotEmpty(protectResult.Nonce);
        Assert.IsNotEmpty(protectResult.Tag);
        Assert.IsNotNull(protectResult.KeyVersion);
        Assert.AreEqual(1, protectResult.KeyVersion);

        var unprotectResult = ut.Unprotect(protectResult);

        Assert.IsInstanceOfType<Secret>(unprotectResult);
        Assert.AreEqual(TEST_KEY, unprotectResult.Reveal());
    }

    [TestMethod]
    public void Protect_SameSecretTwice_ShouldPassIf_ProducesDifferentCiphertext()
    {
        var ut = AesGcmSecretProtectorTestSetup.CreateProtector();

        var secret = new Secret(TEST_KEY);

        var first = ut.Protect(secret);
        var second = ut.Protect(secret);

        Assert.IsFalse(first.Ciphertext.SequenceEqual(second.Ciphertext));
        Assert.IsFalse(first.Nonce.SequenceEqual(second.Nonce));
    }

    [TestMethod]
    public void Protect_ChangeKeyVersion_ShouldPassIf_UsesConfiguredKeyVersion()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector(keyVersion: 7);

        var result = protector.Protect(new Secret(TEST_KEY));

        Assert.AreEqual(7, result.KeyVersion);
    }

    [TestMethod]
    public void Constructor_InvalidKeyLength_ShouldPassIf_ThrowsExpectedException()
    {
        var settings = new EncryptionSettings
        {
            Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)),
            KeyVersion = 1
        };

        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(Options.Create(settings)));
    }

    [TestMethod]
    public void Constructor_InvalidBase64Key_ShouldPassIf_ThrowsExpectedException()
    {
        var settings = new EncryptionSettings
        {
            Key = "this-is-not-base64",
            KeyVersion = 1
        };

        Assert.Throws<InvalidOperationException>(() => new AesGcmSecretProtector(Options.Create(settings)));
    }

    [TestMethod]
    public void Unprotect_TamperedCiphertext_Throws()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector();

        var protectedSecret = protector.Protect(new Secret(TEST_KEY));

        protectedSecret.Ciphertext[0] ^= 0x01;

        Assert.Throws<AuthenticationTagMismatchException>(() => protector.Unprotect(protectedSecret));
    }

    [TestMethod]
    public void Unprotect_WithDifferentKey_ShouldPAssIf_ThrowsExpectedException()
    {
        var originalProtector = AesGcmSecretProtectorTestSetup.CreateProtector();
        var differentProtector = AesGcmSecretProtectorTestSetup.CreateProtector();

        var protectedSecret = originalProtector.Protect(new Secret(TEST_KEY));

        Assert.Throws<AuthenticationTagMismatchException>(() => differentProtector.Unprotect(protectedSecret));
    }

    [TestMethod]
    public void Unprotect_WithDifferentKeyVersion_ShouldPassIf_ThrowsExpectedException()
    {
        var protector = AesGcmSecretProtectorTestSetup.CreateProtector(keyVersion: 1);

        var protectedSecret = protector.Protect(new Secret(TEST_KEY));

        var wrongVersion = protectedSecret with
        {
            KeyVersion = 2 // NOT THE SAME KEY VERSION AS THE PROTECTOR
        };

        Assert.Throws<InvalidOperationException>(() => protector.Unprotect(wrongVersion));
    }
}

class AesGcmSecretProtectorTestSetup
{
    public static AesGcmSecretProtector CreateProtector(int keyVersion = 1)
    {
        var key = RandomNumberGenerator.GetBytes(32);

        var settings = new EncryptionSettings
        {
            Key = Convert.ToBase64String(key),
            KeyVersion = keyVersion,
        };

        return new AesGcmSecretProtector(Options.Create(settings));
    }
}
