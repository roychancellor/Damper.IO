namespace Damper.Domain.Common
{
    public sealed class Secret
    {
        public string Value { get; }

        public Secret(string value)
        {
            Value = value ?? throw new ArgumentNullException(nameof(value));
        }

        public override string ToString() => Masked;

        public static string Masked => "********";
    }
}