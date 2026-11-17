using Damper.Setup;
using Damper.Setup.PostgreSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddOptions<SetupOptions>()
                .Bind(builder.Configuration.GetSection("Setup"))
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

var options = host.Services
                  .GetRequiredService<IOptions<SetupOptions>>()
                  .Value;

Console.WriteLine("Damper.Setup");
Console.WriteLine("------------");
Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");

return 0;