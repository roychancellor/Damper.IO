namespace Damper.Infrastructure.ReferenceData
{
    public class DamperConstants
    {
        public const string REQUEST_X_DAMPER_API_KEY = "X-Damper-Api-Key";
        public const string REQUEST_X_CORRELATION_ID = "X-Correlation-ID";
        public const string REQUEST_CORRELATION_ID = "CorrelationId";
        public const string REQUEST_INTEGRATION_ID = "IntegrationId";
        public const string REQUEST_INTEGRATION_NAME = "IntegrationName";

        public const string DAMPER_SERVICE_NAME = "Damper.OutboundService";
        public const string DAMPER_METER_NAME = "Damper.Core";
        public const string DAMPER_METER_OTLP_ENDPOINT = "http://localhost:4317";
        public const string DAMPER_METER_INTEGRATION_ID = "integration-id";
        public const string DAMPER_METER_INTEGRATION_NAME = "integration-name";
        
        public const string HTTP_CLIENT_NAME = "DamperEgress";

        public const string DAMPER_HEADER_PREFIX = "h_";
        public const string X_DAMPER_API_KEY = "x-damper-api-key";
        public const string X_DAMPER_INTEGRATION_ID = "x-damper-integration-id";
        public const string X_DAMPER_INTEGRATION_NAME = "x-damper-integration-name";
        public const string X_DAMPER_DESTINATION_URL = "x-damper-destination-url";
        public const string X_DAMPER_CORRELATION_ID = "x-damper-correlation-id";
        public const string X_DAMPER_ATTEMPT_COUNT = "x-damper-attempt-count";

        public const string REQUEST_X_DAMPER_CORRELATION_ID = "X-Damper-Correlation-Id";
        public const string REQUEST_X_DAMPER_DELIVERY_ATTEMPT = "X-Damper-Delivery-Attempt";
    }
}