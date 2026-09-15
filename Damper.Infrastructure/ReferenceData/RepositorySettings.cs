namespace Damper.Infrastructure.ReferenceData
{
    public class RepositorySettings
    {
        public int CacheTimeToLiveMinutes { get; set; }

        public string HostName { get; set; } = string.Empty;

        public int Port { get; set; } = 5432;

        public string Database { get; set; } = string.Empty;

        public string UserName { get; set; } = string.Empty;

        public string Password { get; set; } = string.Empty;
    }
}