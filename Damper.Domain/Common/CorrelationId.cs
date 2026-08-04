namespace Damper.Domain.Common
{
    public readonly record struct CorrelationId(string Value)
    {
        public CorrelationId() : this(string.Empty) { }
        
        public override string ToString() => Value;
    }
}