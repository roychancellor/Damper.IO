namespace Damper.Infrastructure.ReferenceData;

public static class OutboundHeaderPolicy
{
    private static readonly HashSet<string> ReservedHeaders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "Authorization",
            "Host",
            "Content-Length",
            "Content-Type",
            "Transfer-Encoding",
            "Connection",
            DamperConstants.REQUEST_X_DAMPER_CORRELATION_ID,
            DamperConstants.REQUEST_X_DAMPER_DELIVERY_ATTEMPT
        };

    public static bool IsReserved(string headerName)
    {
        return ReservedHeaders.Contains(headerName);
    }
}