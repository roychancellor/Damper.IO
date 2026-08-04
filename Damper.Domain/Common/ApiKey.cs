namespace Damper.Domain.Common
{
    public readonly record struct ApiKey(string Value)
    {
        public override string ToString() => Value;
    }
}