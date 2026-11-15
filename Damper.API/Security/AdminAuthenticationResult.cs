namespace Damper.API.Security
{
    public sealed record AdminAuthenticationResult(string Username, IReadOnlyCollection<string> Roles);
}