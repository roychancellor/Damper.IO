namespace Damper.API.Security
{
    public sealed class AdminAuthenticationOptions
    {
        public string Username { get; init; } = string.Empty;
        public string PasswordHash { get; init; } = string.Empty;
    }
}