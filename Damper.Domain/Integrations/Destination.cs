namespace Damper.Domain.Integrations
{
    public sealed class Destination
    {
        public Uri Uri { get; init; } = default!;

        /*
        Future versions might add:
            Primary URI
            Failover URI
            Health Check URI
        */
    }
}