using System.ComponentModel.DataAnnotations;

namespace Damper.API.ViewModels.Integrations;

public sealed class IntegrationHeaderModel
{
    [Required]
    public string Name { get; set; } = string.Empty;

    [Required]
    public string Value { get; set; } = string.Empty;
}