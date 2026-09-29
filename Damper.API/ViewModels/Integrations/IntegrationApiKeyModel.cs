using System.ComponentModel.DataAnnotations;

namespace Damper.API.ViewModels.Integrations;

public sealed class IntegrationApiKeyModel
{
    public long Id { get; set; }

    public string IntegrationName { get; set; } = string.Empty;

    [Required]
    public string ApiKey { get; set; } = string.Empty;
}