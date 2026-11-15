using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace Damper.API.Security
{
    public sealed class ConfiguredAdminCredentialValidator(IOptions<AdminAuthenticationOptions> options,
                                                           IPasswordHasher<AdminAuthenticationResult> passwordHasher)
                        : IAdminCredentialValidator
    {
        private readonly AdminAuthenticationOptions _options = options.Value;

        public Task<AdminAuthenticationResult?> ValidateAsync(string username, string password, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (string.IsNullOrWhiteSpace(username) ||
                string.IsNullOrEmpty(password) ||
                string.IsNullOrWhiteSpace(_options.Username) ||
                string.IsNullOrWhiteSpace(_options.PasswordHash))
            {
                return Task.FromResult<AdminAuthenticationResult?>(null);
            }

            if (!string.Equals(username, _options.Username, StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<AdminAuthenticationResult?>(null);
            }

            var result = new AdminAuthenticationResult(_options.Username, [AdminRoles.Administrator]);

            var verificationResult = passwordHasher.VerifyHashedPassword(result, _options.PasswordHash, password);

            return Task.FromResult<AdminAuthenticationResult?>(
                    verificationResult != PasswordVerificationResult.Failed
                    ? result
                    : null);
        }
    }
}