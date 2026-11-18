using Damper.Setup;
using Damper.Setup.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

var setup = builder.Configuration.GetSection("Setup");
builder.Services.AddOptions<SetupOptions>()
                .Bind(setup)
                .Validate(opt => !string.IsNullOrWhiteSpace(opt.PostgreSql.HostName), "PostgreSQL host is required.")
                .Validate(opt => opt.PostgreSql.Port > 0, "PostgreSQL port is invalid.")
                .Validate(opt => !string.IsNullOrWhiteSpace(opt.PostgreSql.Database), "PostgreSQL database is required.")
                .Validate(opt => !string.IsNullOrWhiteSpace(opt.PostgreSql.UserName), "PostgreSQL username is required.")
                .ValidateOnStart();

builder.Services.AddSingleton(sp =>
{
    var options = sp.GetRequiredService<IOptions<SetupOptions>>().Value;
    return new PostgreSqlInspector(options.PostgreSql);
});

using var host = builder.Build();

try
{
    Console.WriteLine("Damper.Setup");
    Console.WriteLine("------------");
    Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}\n");

    var inspector = host.Services.GetRequiredService<PostgreSqlInspector>();

    await inspector.InspectAsync();

    Console.WriteLine();
    Console.WriteLine("Setup inspection completed successfully.");

    return 0;
}
catch (Exception ex)
{
    Console.Error.WriteLine();
    Console.Error.WriteLine("Damper.Setup failed.");
    Console.Error.WriteLine(ex.Message);

    return 1;
}