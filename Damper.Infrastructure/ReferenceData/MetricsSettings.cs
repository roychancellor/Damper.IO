using System.ComponentModel.DataAnnotations;

namespace Damper.Infrastructure.ReferenceData
{
    public class MetricsSettings
    {
        [StringLength(100, MinimumLength = 10)]
        public string ServiceName { get; set; } = string.Empty;
        
        [StringLength(100, MinimumLength = 10)]
        public string MeterName { get; set; } = string.Empty;
        
        [Url]
        public string OtlpEndpoint { get; set; } = string.Empty;
    }
}