using Damper.API.ViewModels.Integrations;
using Damper.Application.Integrations;
using Damper.Domain.Common;
using Damper.Domain.Integrations;
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
            DestinationUri = integration.Delivery.Destination.Uri.ToString()
        };

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

                Settings = existing.Delivery.Settings,
                Authentication = existing.Delivery.Authentication,
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
}