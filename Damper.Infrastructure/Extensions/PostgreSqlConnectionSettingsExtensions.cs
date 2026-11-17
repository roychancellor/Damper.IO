using Damper.Infrastructure.ReferenceData;
using Npgsql;

namespace Damper.Infrastructure.Extensions
{
    public static class PostgreSqlConnectionSettingsExtensions
    {
        public static string BuildConnectionString(this PostgreSqlConnectionSettings settings)
        {
            ArgumentNullException.ThrowIfNull(settings);

            return new NpgsqlConnectionStringBuilder
            {
                Host = settings.HostName,
                Port = settings.Port,
                Database = settings.Database,
                Username = settings.UserName,
                Password = settings.Password
            }.ConnectionString;
        }
    }
}