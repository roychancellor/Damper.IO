namespace Damper.Application.Integrations;

public sealed class DuplicateApiKeyException : Exception
{
    public DuplicateApiKeyException()
        : base("An Integration already exists with this API key.")
    {
    }

    public DuplicateApiKeyException(Exception innerException)
        : base("An Integration already exists with this API key.", innerException)
    {
    }
}