namespace Damper.API.ViewModels.Integrations;

public sealed class IntegrationHeaderModel
{
    // Make these nullable so the default MVC behavior of requiring them
    // does not occur. The controller owns validation of custom headers.
    public string? Name { get; set; } = string.Empty;

    public string? Value { get; set; } = string.Empty;
}