using CyberHub.ServerService.Services;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/catalog")]
public sealed class ClientCatalogController : ControllerBase
{
    private readonly CatalogSyncService _syncService;
    private readonly RecoveryCodeService _recoveryCodeService;

    public ClientCatalogController(CatalogSyncService syncService, RecoveryCodeService recoveryCodeService)
    {
        _syncService = syncService;
        _recoveryCodeService = recoveryCodeService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PublishedCatalogSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublishedCatalogSnapshot>> GetReadOnlyCatalog(CancellationToken ct)
    {
        PublishedCatalogSnapshot snapshot = await _syncService.BuildReadOnlyCatalogAsync(ct);
        return Ok(snapshot);
    }

    [HttpGet("theme")]
    [ProducesResponseType(typeof(PublishedThemePayload), StatusCodes.Status200OK)]
    public async Task<ActionResult<PublishedThemePayload>> GetReadOnlyPublishedTheme(CancellationToken ct)
    {
        PublishedThemePayload theme = await _syncService.BuildReadOnlyThemeAsync(ct);
        return Ok(theme);
    }

    [HttpPost("verify-recovery")]
    public ActionResult<object> VerifyStationRecovery([FromBody] VerifyRecoveryRequest request)
    {
        bool valid = _recoveryCodeService.VerifyStationRecoveryCode(request.StationName, request.RecoveryCode);
        if (!valid)
        {
            return Unauthorized(new { authorized = false });
        }

        return Ok(new { authorized = true });
    }
}
