using Npgsql;

namespace Damper.Tests.IntegrationTests.PostgreSql;

[TestClass]
public sealed class PostgreSqlBootstrapTests
{
    private static PostgreSqlTestEnvironment _environment = null!;

    public TestContext TestContext { get; set; }

    [ClassInitialize]
    public static async Task ClassInitialize(TestContext _)
    {
        _environment = new PostgreSqlTestEnvironment();
        await _environment.StartAsync();
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

    [TestMethod]
    public async Task Bootstrap_ShouldPassIf_RuntimeCanConnect()
    {
        await using var connection = new NpgsqlConnection(_environment.GetRuntimeConnectionString());
        await connection.OpenAsync();

        await using var command = new NpgsqlCommand("SELECT current_user;", connection);

        var currentUser = (string?)await command.ExecuteScalarAsync(TestContext.CancellationToken);

        Assert.AreEqual("damper-runtime", currentUser);
    }

    [TestMethod]
    public async Task Bootstrap_ShouldPassIf_IntegrationFunctionExists()
    {
        await using var connection = new NpgsqlConnection(_environment.GetRuntimeConnectionString());
        await connection.OpenAsync(TestContext.CancellationToken);

        await using var command = new NpgsqlCommand("SELECT COUNT(*) FROM damper.integration_get_all();", connection);

        var count = (long)(await command.ExecuteScalarAsync(TestContext.CancellationToken) ?? -1L);

        Assert.AreEqual(0L, count);
    }
}