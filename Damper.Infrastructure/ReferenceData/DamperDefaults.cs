namespace Damper.Infrastructure.ReferenceData
{
    public class DamperDefaults
    {
        public const string DAMPER_SERVICE_NAME = "Damper.OutboundService";
        public const string DAMPER_METER_NAME = "Damper.Core";
        public const string DAMPER_METER_OTLP_ENDPOINT = "http://localhost:4317";
        
        public const string HTTP_CLIENT_NAME = "DamperEgress";

        public const string DAMPER_HEADER_PREFIX = "h_";
        public const string X_DAMPER_CUSTOMER_ID = "x-damper-customer-id";
        public const string X_DAMPER_DESTINATION_URL = "x-damper-destination-url";
        public const string X_DAMPER_CORRELATION_ID = "x-damper-correlation-id";
        public const string X_DAMPER_ATTEMPT_COUNT = "x-damper-attempt-count";

        public const string REQUEST_X_DAMPER_CUSTOMER_ID = "X-Damper-Customer-Id";
        public const string REQUEST_X_DAMPER_DELIVERY_ATTEMPT = "X-Damper-Delivery-Attempt";
    }
}