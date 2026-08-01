namespace Damper.Domain
{
    public sealed class MessageDestination
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