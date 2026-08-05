namespace Damper.Domain.Enums
{
    public enum AuthenticationType
    {
        None,
        ApiKey,
        Basic,
        Bearer,
        CustomHeader,
        MutualTls
    }
}