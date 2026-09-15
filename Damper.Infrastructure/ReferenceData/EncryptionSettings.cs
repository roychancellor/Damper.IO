namespace Damper.Infrastructure.ReferenceData;

public sealed class EncryptionSettings
{
    public string Key { get; set; } = string.Empty;

    public int KeyVersion { get; set; } = 1;
}
