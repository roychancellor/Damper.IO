using System.Security.Cryptography;

namespace Damper.Domain.Common;

public readonly struct ApiKeyHash : IEquatable<ApiKeyHash>
{
    public const int Length = 32;

    private readonly byte[]? _value;

    public ApiKeyHash(byte[] value)
    {
        ArgumentNullException.ThrowIfNull(value);

        if (value.Length != Length)
        {
            throw new ArgumentException(
                $"An API key hash must be exactly {Length} bytes.",
                nameof(value));
        }

        // Preserve value-object semantics. The caller cannot modify our copy.
        _value = value.ToArray();
    }

    public bool IsEmpty => _value is null || _value.Length == 0;

    public byte[] ToArray()
    {
        return _value?.ToArray() ?? [];
    }

    public override string ToString()
    {
        return IsEmpty ? string.Empty : Convert.ToHexString(_value!);
    }

    public bool Equals(ApiKeyHash other)
    {
        if (IsEmpty || other.IsEmpty)
        {
            return IsEmpty && other.IsEmpty;
        }

        return CryptographicOperations.FixedTimeEquals(_value!, other._value!);
    }

    public override bool Equals(object? obj)
    {
        return obj is ApiKeyHash other && Equals(other);
    }

    public override int GetHashCode()
    {
        if (IsEmpty)
        {
            return 0;
        }

        var hashCode = new HashCode();

        foreach (var value in _value!)
        {
            hashCode.Add(value);
        }

        return hashCode.ToHashCode();
    }

    public static bool operator ==(ApiKeyHash left, ApiKeyHash right)
    {
        return left.Equals(right);
    }

    public static bool operator !=(ApiKeyHash left, ApiKeyHash right)
    {
        return !left.Equals(right);
    }
}