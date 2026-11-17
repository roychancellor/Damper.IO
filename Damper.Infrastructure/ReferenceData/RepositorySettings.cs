namespace Damper.Infrastructure.ReferenceData
{
    public class RepositorySettings : PostgreSqlConnectionSettings
    {
        public int CacheTimeToLiveMinutes { get; set; }
    }
}