namespace Damper.Infrastructure.ReferenceData
{
    public class PostgreSqlConnectionSettings
    {
        public string HostName { get; set; } = string.Empty;

        public int Port { get; set; } = 5432;

        public string Database { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}