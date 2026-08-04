using System.Security.Cryptography;
using System.Text;

namespace Damper.Domain.Common
{
    public readonly struct ApiKey(string value) : IEquatable<ApiKey>
    {
        // Parameterless constructor allows "new()" without creating a null for _value
        public ApiKey() : this(string.Empty) { }

        private readonly string _value = value ?? string.Empty;

        // Deliberate, auditable - anyone reading a call site immediately sees
        // "this code is intentionally touching the real value."
        public string Reveal() => _value;

        public override string ToString() => Masked;

        public string Masked => _value.Length > 4 ? $"****{_value[^4..]}" : "****";

        public bool Equals(ApiKey other) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_value), Encoding.UTF8.GetBytes(other._value));

        public override bool Equals(object? obj) => obj is ApiKey k && Equals(k);

        public override int GetHashCode() => 0; // deliberately non-value-based

        public static bool operator ==(ApiKey left, ApiKey right) => left.Equals(right);
        public static bool operator !=(ApiKey left, ApiKey right) => !left.Equals(right);
    }
}