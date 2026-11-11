using Damper.API.ViewModels.Integrations;
using Damper.Application.Integrations;
using Damper.Domain.Common;
using Damper.Domain.Enums;
using Damper.Domain.Integrations;
using Damper.Domain.Integrations.OutAuthentication;
using Damper.Infrastructure.Security;
using Microsoft.AspNetCore.Mvc;

namespace Damper.API.Controllers;

public sealed class IntegrationsController : Controller
{
    private readonly IIntegrationService _integrationService;

    public IntegrationsController(IIntegrationService integrationService)
    {
        _integrationService = integrationService;
    }

    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var integrations = await _integrationService.GetAllAsync(cancellationToken);

        return View(integrations);
    }

    public async Task<IActionResult> Edit(long id, CancellationToken cancellationToken)
    {
        var integration = await _integrationService.GetByIdAsync(id, cancellationToken);

        if (integration == null)
        {
            return NotFound();
        }

        var model = new IntegrationEditModel
        {
            Id = integration.Id,
            Name = integration.Name.ToString(),
            Description = integration.Description,
            Enabled = integration.Enabled,
            IngressEnabled = integration.Ingress.Enabled,
            DeliveryEnabled = integration.Delivery.Enabled,
            DestinationUri = integration.Delivery.Destination.Uri.ToString(),

            // Delivery settings
            RequestsPerInterval = integration.Delivery.Settings.RequestsPerInterval,
            DeliveryIntervalMillis = integration.Delivery.Settings.DeliveryIntervalMillis,
            MaxRetryAttempts = integration.Delivery.Settings.MaxRetryAttempts,
            InitialRetryDelayMillis = integration.Delivery.Settings.InitialRetryDelayMillis,
            RetryBackoffMultiplier = integration.Delivery.Settings.RetryBackoffMultiplier,
            MaximumRetryDelayMillis = integration.Delivery.Settings.MaximumRetryDelayMillis,
            RequestTimeoutMillis = integration.Delivery.Settings.RequestTimeoutMillis,
            MaxQueueCapacity = integration.Delivery.Settings.MaxQueueCapacity,

            // Authentication settings
            AuthenticationType = integration.Delivery.Authentication.Type
        };

        switch (integration.Delivery.Authentication)
        {
            case BasicAuthentication basic:
                model.AuthenticationUsername = basic.Username;
                break;

            case CustomHeaderAuthentication customHeader:
                model.AuthenticationHeaderName = customHeader.HeaderName;
                break;
        }

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(IntegrationEditModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await _integrationService.GetByIdAsync(model.Id, cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        if (!Uri.TryCreate(model.DestinationUri, UriKind.Absolute, out var destinationUri))
        {
            ModelState.AddModelError(nameof(model.DestinationUri), "Destination URI must be a valid absolute URI.");

            return View(model);
        }

        var newAuthentication = BuildAuthentication(model, existing.Delivery.Authentication);

        if (newAuthentication == null)
        {
            model.AuthenticationSecret = null;
            return View(model);
        }

        var updated = new Integration
        {
            Id = existing.Id,
            Name = new IntegrationName(model.Name),
            Description = model.Description,
            Enabled = model.Enabled,

            Ingress = new Ingress
            {
                Enabled = model.IngressEnabled,
                ApiKeyHash = existing.Ingress.ApiKeyHash
            },

            Delivery = new Delivery
            {
                Enabled = model.DeliveryEnabled,

                Destination = new Destination
                {
                    Uri = destinationUri
                },

                Settings = new DeliverySettings
                {
                    RequestsPerInterval = model.RequestsPerInterval,
                    DeliveryIntervalMillis = model.DeliveryIntervalMillis,
                    MaxRetryAttempts = model.MaxRetryAttempts,
                    InitialRetryDelayMillis = model.InitialRetryDelayMillis,
                    RetryBackoffMultiplier = model.RetryBackoffMultiplier,
                    MaximumRetryDelayMillis = model.MaximumRetryDelayMillis,
                    RequestTimeoutMillis = model.RequestTimeoutMillis,
                    MaxQueueCapacity = model.MaxQueueCapacity
                },

                Authentication = newAuthentication,

                Headers = existing.Delivery.Headers
            },

            CreatedUtc = existing.CreatedUtc,
            ModifiedUtc = existing.ModifiedUtc
        };

        await _integrationService.SaveAsync(updated, cancellationToken);

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetEnabled(long id, bool enabled, CancellationToken cancellationToken)
    {
        var existing = await _integrationService.GetByIdAsync(id, cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        var updated = new Integration
        {
            Id = existing.Id,
            Name = existing.Name,
            Description = existing.Description,
            Enabled = enabled,
            Ingress = existing.Ingress,
            Delivery = existing.Delivery,
            CreatedUtc = existing.CreatedUtc,
            ModifiedUtc = existing.ModifiedUtc
        };

        await _integrationService.SaveAsync(updated, cancellationToken);

        return RedirectToAction(nameof(Index));
    }

    public IActionResult Create()
    {
        return View(new IntegrationCreateModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(IntegrationCreateModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        if (!Uri.TryCreate(model.DestinationUri, UriKind.Absolute, out var destinationUri))
        {
            ModelState.AddModelError(nameof(model.DestinationUri), "Destination URI must be a valid absolute URI.");

            return View(model);
        }

        var integration = new Integration
        {
            Name = new IntegrationName(model.Name),
            Description = model.Description,
            Enabled = model.Enabled,

            Ingress = new Ingress
            {
                Enabled = model.IngressEnabled,
                ApiKeyHash = new ApiKey(model.ApiKey).ToHash()
            },

            Delivery = new Delivery
            {
                Enabled = model.DeliveryEnabled,
                Destination = new Destination
                {
                    Uri = destinationUri
                },
                Authentication = new NoAuthentication(),
                Headers = new HeaderCollection(),
                Settings = new DeliverySettings()
            }
        };

        try
        {
            await _integrationService.SaveAsync(integration, cancellationToken);
        }
        catch (DuplicateApiKeyException)
        {
            ModelState.AddModelError(nameof(model.ApiKey), "This API key is already in use.");

            return View(model);
        }
        catch (Exception)
        {
            throw;
        }

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> Delete(long id, CancellationToken cancellationToken)
    {
        var integration = await _integrationService.GetByIdAsync(id, cancellationToken);

        if (integration == null)
        {
            return NotFound();
        }

        return View(integration);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(long id, CancellationToken cancellationToken)
    {
        var integration = await _integrationService.GetByIdAsync(id, cancellationToken);

        if (integration == null)
        {
            return NotFound();
        }

        await _integrationService.DeleteAsync(id, cancellationToken);

        return RedirectToAction(nameof(Index));
    }

    public async Task<IActionResult> RotateApiKey(long id, CancellationToken cancellationToken)
    {
        var integration = await _integrationService.GetByIdAsync(id, cancellationToken);

        if (integration == null)
        {
            return NotFound();
        }

        var model = new IntegrationApiKeyModel
        {
            Id = integration.Id,
            IntegrationName = integration.Name.ToString()
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RotateApiKey(IntegrationApiKeyModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var existing = await _integrationService.GetByIdAsync(model.Id, cancellationToken);

        if (existing == null)
        {
            return NotFound();
        }

        var updated = new Integration
        {
            Id = existing.Id,
            Name = existing.Name,
            Description = existing.Description,
            Enabled = existing.Enabled,

            Ingress = new Ingress
            {
                Enabled = existing.Ingress.Enabled,
                ApiKeyHash = new ApiKey(model.ApiKey).ToHash()
            },

            Delivery = existing.Delivery,

            CreatedUtc = existing.CreatedUtc,
            ModifiedUtc = existing.ModifiedUtc
        };

        try
        {
            await _integrationService.SaveAsync(updated, cancellationToken);
        }
        catch (DuplicateApiKeyException)
        {
            ModelState.AddModelError(nameof(model.ApiKey), "This API key is already in use.");

            return View(model);
        }

        return RedirectToAction(nameof(Edit), new { id = model.Id });
    }

    #region Helper Methods
    private OutboundAuthentication? BuildAuthentication(IntegrationEditModel model, OutboundAuthentication existing)
    {
        /*
         * Existing secrets stay entirely on the server.
         * A blank secret means "keep the existing secret" only when the auth type is unchanged.
         */
        switch (model.AuthenticationType)
        {
            case AuthenticationType.None: return new NoAuthentication();
            
            case AuthenticationType.Basic: // Username + Password
                if (string.IsNullOrWhiteSpace(model.AuthenticationUsername))
                {
                    ModelState.AddModelError(nameof(model.AuthenticationUsername),
                                             "Username is required for Basic authentication.");
                    return null;
                }

                // Only change the password when the user enters a new one
                if (!string.IsNullOrWhiteSpace(model.AuthenticationSecret))
                {
                    return new BasicAuthentication
                    {
                        Username = model.AuthenticationUsername,
                        Password = new Secret(model.AuthenticationSecret)
                    };
                }

                // Otherwise, keep the existing password
                if (existing is BasicAuthentication existingBasic)
                {
                    return new BasicAuthentication
                    {
                        Username = model.AuthenticationUsername,
                        Password = existingBasic.Password
                    };
                }

                ModelState.AddModelError(nameof(model.AuthenticationSecret),
                                         "A password is required when changing to Basic authentication.");
                return null;

            case AuthenticationType.Bearer:
                // Only change the secret if the user entered a new one
                if (!string.IsNullOrWhiteSpace(model.AuthenticationSecret))
                {
                    return new BearerAuthentication
                    {
                        Token = new Secret(model.AuthenticationSecret)
                    };
                }

                // Otherwise, keep the existing secret
                if (existing is BearerAuthentication existingBearer)
                {
                    return existingBearer;
                }

                ModelState.AddModelError(nameof(model.AuthenticationSecret),
                                         "A token is required when changing to Bearer authentication.");
                return null;

            case AuthenticationType.CustomHeader:
                if (string.IsNullOrWhiteSpace(model.AuthenticationHeaderName))
                {
                    ModelState.AddModelError(nameof(model.AuthenticationHeaderName),
                                             "Header name is required for Custom Header authentication.");
                    return null;
                }

                // Only change the header value if the user entered a new one
                if (!string.IsNullOrWhiteSpace(model.AuthenticationSecret))
                {
                    return new CustomHeaderAuthentication
                    {
                        HeaderName = model.AuthenticationHeaderName,
                        HeaderValue = new Secret(model.AuthenticationSecret)
                    };
                }

                // Otherwise, keep the existing header value
                if (existing is CustomHeaderAuthentication existingCustomHeader)
                {
                    return new CustomHeaderAuthentication
                    {
                        HeaderName = model.AuthenticationHeaderName,
                        HeaderValue = existingCustomHeader.HeaderValue
                    };
                }

                ModelState.AddModelError(nameof(model.AuthenticationSecret),
                                         "A header value is required when changing to Custom Header authentication.");
                return null;

            default:
                ModelState.AddModelError(nameof(model.AuthenticationType),
                                         "Unsupported authentication type.");
                return null;
        }
    }
    #endregion
}