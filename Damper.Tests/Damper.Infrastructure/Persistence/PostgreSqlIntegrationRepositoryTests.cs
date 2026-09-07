using Damper.Domain.Common;
using Damper.Domain.Integrations;
using Damper.Domain.Integrations.OutAuthentication;
using Damper.Infrastructure.Persistence.PostgreSql;
using Damper.Infrastructure.ReferenceData;
using Damper.Infrastructure.Security;
using Damper.Tests.IntegrationTests.PostgreSql;
using Dapper;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;

namespace Damper.Tests.Damper.Infrastructure.Persistence;

[TestClass]
public sealed class PostgreSqlIntegrationRepositoryTests
{
    private static PostgreSqlTestEnvironment _environment = null!;
    private static PostgreSqlIntegrationRepository _repository = null!;

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        DefaultTypeMap.MatchNamesWithUnderscores = true;

        _environment = new PostgreSqlTestEnvironment();
        await _environment.StartAsync();

        var repositoryOptions = Options.Create(_environment.GetRuntimeRepositorySettings());

        var encryptionSettings = new EncryptionSettings
        {
            Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)),
            KeyVersion = 1
        };

        var secretProtector = new AesGcmSecretProtector(Options.Create(encryptionSettings));

        _repository = new PostgreSqlIntegrationRepository(repositoryOptions, secretProtector);
    }

    [ClassCleanup]
    public static async Task ClassCleanup()
    {
        await _environment.DisposeAsync();
    }

    [TestInitialize]
    public async Task TestInitialize()
    {
        await _environment.ResetAsync();
    }
}