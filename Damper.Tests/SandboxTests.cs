using Damper.API.Security;
using Microsoft.AspNetCore.Identity;

namespace Damper.Tests;

[TestClass]
public sealed class SandboxTests
{
    [TestMethod]
    public void CreatePasswordHash()
    {
        var hasher = new PasswordHasher<AdminAuthenticationResult>();

        var result = new AdminAuthenticationResult(
            "admin",
            [AdminRoles.Administrator]);

        var hash = hasher.HashPassword(result, "abc123");

        Console.WriteLine(hash);
    }
}
