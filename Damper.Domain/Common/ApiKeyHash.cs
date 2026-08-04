namespace Damper.Domain.Common
{
    public readonly struct ApiKeyHash(string value)
    {
        public ApiKeyHash() : this(string.Empty) { }

        private readonly string _value = value ?? string.Empty;

        public override string ToString() => _value;
    }
}