using CyberHub.ServerService.Services;
using CyberHub.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class ClientCatalogController : ControllerBase
{
    private readonly CatalogSyncService _syncService;

    public ClientCatalogController(CatalogSyncService syncService)
    {
        _syncService = syncService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PublishedCatalogSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublishedCatalogSnapshot>> GetReadOnlyCatalog(CancellationToken ct)
    {
        PublishedCatalogSnapshot snapshot = await _syncService.BuildReadOnlyCatalogAsync(ct);
        return Ok(snapshot);
    }
}
