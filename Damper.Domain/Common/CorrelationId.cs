namespace Damper.Domain.Common
{
    public readonly record struct CorrelationId(string Value)
    {
        public override string ToString() => Value;
    }
}