using System.ComponentModel.DataAnnotations;

namespace Damper.Infrastructure.ReferenceData
{
    public class AppSettings
    {
        [Required]
        public RepositorySettings RepositorySettings { get; set; } = new();
        
        [Required]
        public RabbitMQSettings RabbitMqSettings { get; set; } = new();

        [Required]
        public ProcessorSettings ProcessorSettings { get; set; } = new();
        
        [Required]
        public MetricsSettings MetricsSettings { get; set; } = new();
        
        [Required]
        public EgressSettings EgressSettings { get; set; } = new();

        [Required]
        public EncryptionSettings EncryptionSettings { get; set; } = new();
    }
}