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
}