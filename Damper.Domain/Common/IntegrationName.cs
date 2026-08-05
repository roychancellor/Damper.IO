namespace Damper.Domain.Common
{
    public readonly record struct IntegrationName(string Value)
    {
        public IntegrationName() : this(string.Empty) { }
        
        public override string ToString() => Value;
    }
}