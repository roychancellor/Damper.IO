using System.Security.Cryptography;
using System.Text;

namespace Damper.Core.Verifiers
{
    /*
        By business rule, Damper.IO will NOT perform webhook signature verification.
        This is in keeping with the core concept of not opening the mail, lest Damper
        becomes responsible for the data and resulting decisions. Also, it would require
        customers to provide their webhook key to Damper, making Damper liable for
        secure storage of the keys.

        Keeping this class around in case a future revision will offer signature
        verification as a service. Would need to develop a custom verifier for
        each webhook provider Damper supports (which is another reason to initially
        release without this feature).
    */
    
    public class ShopifyWebhookVerifier : ISignatureVerifier
    {
        public bool VerifyHmacSignature(string payload, string incomingSignature, string secretKey)
        {
            // Defend against null or obviously malformed signatures immediately
            if (string.IsNullOrEmpty(incomingSignature) || incomingSignature.Length != 64)
            {
                return false;
            }

            // Convert incoming hex string straight to bytes without string allocations
            byte[] incomingBytes;
            try
            {
                incomingBytes = Convert.FromHexString(incomingSignature);
            }
            catch (FormatException)
            {
                return false; // Not a valid hex string
            }

            // Compute the native hash
            var keyBytes = Encoding.UTF8.GetBytes(secretKey);
            var payloadBytes = Encoding.UTF8.GetBytes(payload);

            using var hmac = new HMACSHA256(keyBytes);
            var computedHashBytes = hmac.ComputeHash(payloadBytes);

            // Compare the two native byte arrays directly in constant time
            return CryptographicOperations.FixedTimeEquals(computedHashBytes, incomingBytes);
        }
    }
}