using System.Security.Cryptography;

namespace Damper.Core.Utilities;

public static class CorrelationIdGenerator
{
    private static readonly char[] Characters = "ABCDEFGHJKMNPQRSTUVWXYZ23456789".ToCharArray();

    public static string Generate()
    {
        var result = new char[10];
        var randomBytes = new byte[10];
        
        RandomNumberGenerator.Fill(randomBytes);

        for (int i = 0; i < 10; i++)
        {
            result[i] = Characters[randomBytes[i] % Characters.Length];
        }

        return new string(result);
    }
}