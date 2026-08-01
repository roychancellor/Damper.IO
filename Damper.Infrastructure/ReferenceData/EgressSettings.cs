using System.ComponentModel.DataAnnotations;

namespace Damper.Infrastructure.ReferenceData
{
    public class EgressSettings
    {
        [Required]
        public string HttpClientName { get; set; } = string.Empty;
        
        [Range(1, int.MaxValue)]
        public int PooledConnectionLifetimeSeconds { get; set; }
        
        [Range(1, int.MaxValue)]
        public int PooledConnectionIdleTimeoutSeconds { get; set; }
        
        [Range(1, int.MaxValue)]
        public int MaxConnectionsPerServer { get; set; }
        
        [Required]
        public bool EnableMultipleHttp2Connections { get; set; }
        
        [Range(1, int.MaxValue)]
        public int HandlerLifetimeSeconds { get; set; }

        [Range(1, int.MaxValue)]
        public int MaxSendAttempts { get; set; }

        [Range(1, int.MaxValue)]
        public int RequestTimeoutMillis { get; set; }

        [Range(10, int.MaxValue)]
        public int RetryBackoffMillis { get; set; }

        [Range(0, int.MaxValue)]
        public int RetryBackoffJitterMillis { get; set; }

        [Required]
        public HashSet<string> SystemHeaders { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        [Range(0, int.MaxValue)]
        public int CircuitBreakerCooldownSeconds { get; set; }
    }
}