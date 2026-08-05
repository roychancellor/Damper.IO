using System.Security.Cryptography;
using System.Text;

namespace Damper.Domain.Common
{
    public readonly struct Secret(string value) : IEquatable<Secret>
    {
        // Parameterless constructor allows "new()" without creating a null for _value
        public Secret() : this(string.Empty) { }
        
        private readonly string _value = value ?? string.Empty;

        // Deliberate, auditable - anyone reading a call site immediately sees
        // "this code is intentionally touching the real value."
        public string Reveal() => _value;

        public override string ToString() => "[REDACTED]";

        public bool Equals(Secret other) => CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(_value), Encoding.UTF8.GetBytes(other._value));

        public override bool Equals(object? obj) => obj is Secret s && Equals(s);
        public override int GetHashCode() => 0; // deliberately non-value-based - see note below

        public static bool operator ==(Secret left, Secret right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(Secret left, Secret right)
        {
            return !(left == right);
        }
    }
}