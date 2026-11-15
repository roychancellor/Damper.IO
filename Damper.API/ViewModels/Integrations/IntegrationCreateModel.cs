using System.ComponentModel.DataAnnotations;

namespace Damper.API.ViewModels.Integrations;

public sealed class IntegrationCreateModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    public bool Enabled { get; set; } = true;

    public bool IngressEnabled { get; set; } = true;

    public bool DeliveryEnabled { get; set; } = true;

    [Required]
    [Url]
    public string DestinationUri { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}