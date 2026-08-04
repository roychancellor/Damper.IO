namespace Damper.Domain.Common
{
    public readonly record struct IntegrationName(string Value)
    {
        public override string ToString() => Value;
    }
}