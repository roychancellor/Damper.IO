namespace Damper.API.Security
{
    public interface IAdminCredentialValidator
    {
        Task<AdminAuthenticationResult?> ValidateAsync(
            string username,
            string password,
            CancellationToken cancellationToken = default);
    }
}