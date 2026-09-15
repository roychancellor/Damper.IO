using Damper.Domain.Common;
using Damper.Domain.Enums;
using Damper.Domain.Integrations;
using Damper.Domain.Integrations.OutAuthentication;
using Damper.Infrastructure.Security;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Damper.Infrastructure.Persistence.PostgreSql;

internal sealed class IntegrationDocument
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static IntegrationDocument FromJson(string json)
    {
        return JsonSerializer.Deserialize<IntegrationDocument>(json, JsonOptions)
               ?? throw new InvalidOperationException("Integration configuration could not be deserialized.");
    }

    public string? Description { get; init; }

    public bool IngressEnabled { get; init; } = true;

    public bool DeliveryEnabled { get; init; } = true;

    public string DestinationUri { get; init; } = string.Empty;

    public int RequestsPerInterval { get; init; }

    public int DeliveryIntervalMillis { get; init; }

    public int MaxRetryAttempts { get; init; }

    public int InitialRetryDelayMillis { get; init; }

    public double RetryBackoffMultiplier { get; init; }

    public long MaximumRetryDelayMillis { get; init; }

    public int RequestTimeoutMillis { get; init; }

    public int MaxQueueCapacity { get; init; }

    public AuthenticationDocument Authentication { get; init; } = new();

    public Dictionary<string, string> Headers { get; init; } = [];

    public static IntegrationDocument FromDomain(Integration integration, ISecretProtector secretProtector)
    {
        ArgumentNullException.ThrowIfNull(integration);
        ArgumentNullException.ThrowIfNull(secretProtector);

        return new IntegrationDocument
        {
            Description = integration.Description,
            IngressEnabled = integration.Ingress.Enabled,
            DeliveryEnabled = integration.Delivery.Enabled,
            DestinationUri = integration.Delivery.Destination.Uri.ToString(),
            RequestsPerInterval = integration.Delivery.Settings.RequestsPerInterval,
            DeliveryIntervalMillis = integration.Delivery.Settings.DeliveryIntervalMillis,
            MaxRetryAttempts = integration.Delivery.Settings.MaxRetryAttempts,
            InitialRetryDelayMillis = integration.Delivery.Settings.InitialRetryDelayMillis,
            RetryBackoffMultiplier = integration.Delivery.Settings.RetryBackoffMultiplier,
            MaximumRetryDelayMillis = integration.Delivery.Settings.MaximumRetryDelayMillis,
            RequestTimeoutMillis = integration.Delivery.Settings.RequestTimeoutMillis,
            MaxQueueCapacity = integration.Delivery.Settings.MaxQueueCapacity,
            Authentication = ToAuthenticationDocument(integration.Delivery.Authentication, secretProtector),
            Headers = new Dictionary<string, string>(integration.Delivery.Headers.Headers)
        };
    }

    private static AuthenticationDocument ToAuthenticationDocument(OutboundAuthentication authentication, ISecretProtector secretProtector)
    {
        return authentication switch
        {
            NoAuthentication => new AuthenticationDocument
            {
                Type = AuthenticationType.None
            },

            BasicAuthentication basic => new AuthenticationDocument
            {
                Type = AuthenticationType.Basic,
                Username = basic.Username,
                Secret = secretProtector.Protect(basic.Password)
            },

            BearerAuthentication bearer => new AuthenticationDocument
            {
                Type = AuthenticationType.Bearer,
                Secret = secretProtector.Protect(bearer.Token)
            },

            CustomHeaderAuthentication customHeader =>
                new AuthenticationDocument
                {
                    Type = AuthenticationType.CustomHeader,
                    HeaderName = customHeader.HeaderName,
                    Secret = secretProtector.Protect(
                        customHeader.HeaderValue)
                },

            _ => throw new NotSupportedException($"Unsupported authentication type: {authentication.GetType().Name}")
        };
    }

    public Integration ToDomain(IntegrationRecord record, ISecretProtector secretProtector)
    {
        ArgumentNullException.ThrowIfNull(record);
        ArgumentNullException.ThrowIfNull(secretProtector);

        return new Integration
        {
            Id = record.Id,
            Name = new IntegrationName(record.Name),
            Description = Description,
            Enabled = record.Enabled,
            Ingress = new Ingress
            {
                Enabled = IngressEnabled,
                ApiKeyHash = new ApiKeyHash(record.ApiKeyHash)
            },
            Delivery = new Delivery
            {
                Enabled = DeliveryEnabled,
                Destination = new Destination { Uri = new Uri(DestinationUri) },
                Settings = new DeliverySettings
                {
                    RequestsPerInterval = RequestsPerInterval,
                    DeliveryIntervalMillis = DeliveryIntervalMillis,
                    MaxRetryAttempts = MaxRetryAttempts,
                    InitialRetryDelayMillis = InitialRetryDelayMillis,
                    RetryBackoffMultiplier = RetryBackoffMultiplier,
                    MaximumRetryDelayMillis = MaximumRetryDelayMillis,
                    RequestTimeoutMillis = RequestTimeoutMillis,
                    MaxQueueCapacity = MaxQueueCapacity
                },
                Authentication = ToDomainAuthentication(Authentication, secretProtector),
                Headers = new HeaderCollection { Headers = Headers },
            },
            CreatedUtc = record.CreatedAt,
            ModifiedUtc = record.ModifiedAt
        };
    }

    private static OutboundAuthentication ToDomainAuthentication(AuthenticationDocument authentication, ISecretProtector secretProtector)
    {
        return authentication.Type switch
        {
            AuthenticationType.None => new NoAuthentication(),

            AuthenticationType.Basic => new BasicAuthentication
            {
                Username = authentication.Username ?? string.Empty,
                Password = secretProtector.Unprotect(authentication.Secret ?? throw new InvalidOperationException("Basic authentication secret is missing."))
            },

            AuthenticationType.Bearer => new BearerAuthentication
            {
                Token = secretProtector.Unprotect(authentication.Secret ?? throw new InvalidOperationException("Bearer authentication secret is missing."))
            },

            AuthenticationType.CustomHeader => new CustomHeaderAuthentication
            {
                HeaderName = authentication.HeaderName ?? string.Empty,
                HeaderValue = secretProtector.Unprotect(authentication.Secret ?? throw new InvalidOperationException("Custom header authentication secret is missing."))
            },

            _ => throw new NotSupportedException($"Unsupported authentication type: {authentication.Type}")
        };
    }
}

internal sealed class AuthenticationDocument
{
    public AuthenticationType Type { get; init; }

    public string? Username { get; init; }

    public string? HeaderName { get; init; }

    public ProtectedSecret? Secret { get; init; }
}