using Damper.Domain.Enums;
using System.ComponentModel.DataAnnotations;

namespace Damper.API.ViewModels.Integrations;

public sealed class IntegrationEditModel
{
    public long Id { get; set; }

    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Enabled { get; set; }

    public bool IngressEnabled { get; set; }

    public bool DeliveryEnabled { get; set; }

    [Required]
    [Url]
    public string DestinationUri { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int RequestsPerInterval { get; set; }

    [Range(1, int.MaxValue)]
    public int DeliveryIntervalMillis { get; set; }

    [Range(0, int.MaxValue)]
    public int MaxRetryAttempts { get; set; }

    [Range(1, int.MaxValue)]
    public int InitialRetryDelayMillis { get; set; }

    [Range(1.0, 100.0)]
    public double RetryBackoffMultiplier { get; set; }

    [Range(1, long.MaxValue)]
    public long MaximumRetryDelayMillis { get; set; }

    [Range(1, int.MaxValue)]
    public int RequestTimeoutMillis { get; set; }

    [Range(1, int.MaxValue)]
    public int MaxQueueCapacity { get; set; }

    // Authentication properties
    public AuthenticationType AuthenticationType { get; set; }

    public string? AuthenticationUsername { get; set; }

    public string? AuthenticationSecret { get; set; }

    public string? AuthenticationHeaderName { get; set; }

    // Custom Outbound Headers
    public List<IntegrationHeaderModel> Headers { get; set; } = [];
}