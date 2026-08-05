namespace Damper.Domain.Integrations
{
        public sealed class HeaderCollection
    {
        public IDictionary<string, string> Headers { get; init; } = new Dictionary<string, string>();
    }
}