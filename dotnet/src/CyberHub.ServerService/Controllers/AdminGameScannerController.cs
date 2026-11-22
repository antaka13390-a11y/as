using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.ServerService.Services;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/admin/scanner")]
public sealed class AdminGameScannerController : ControllerBase
{
    private readonly SqliteGameRepository _repository;
    private readonly GameScannerService _scanner;
    private readonly CoverArtLookupService _coverLookup;
    private readonly ServerOptions _options;

    public AdminGameScannerController(
        SqliteGameRepository repository,
        GameScannerService scanner,
        CoverArtLookupService coverLookup,
        IOptions<ServerOptions> options)
    {
        _repository = repository;
        _scanner = scanner;
        _coverLookup = coverLookup;
        _options = options.Value;
    }

    [HttpGet("storage-locations")]
    public async Task<ActionResult<List<StorageLocation>>> GetStorageLocations(CancellationToken ct)
    {
        return Ok(await _repository.GetStorageLocationsAsync(ct));
    }

    [HttpPost("storage-locations")]
    public async Task<ActionResult<StorageLocation>> SaveStorageLocation(
        [FromBody] UpsertStorageLocationRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.DisplayName))
        {
            return BadRequest(new { error = "Display name is required." });
        }

        (bool valid, string error) = UncPathValidator.ValidateStorageRootPath(request.UncRootPath);
        if (!valid)
        {
            return BadRequest(new { error });
        }

        var location = new StorageLocation
        {
            Id = request.Id,
            DisplayName = request.DisplayName.Trim(),
            UncRootPath = request.UncRootPath.Trim(),
            Description = request.Description.Trim(),
            IsReadOnlyForClients = request.IsReadOnlyForClients
        };

        StorageLocation saved = await _repository.UpsertStorageLocationAsync(location, ct);
        return Ok(saved);
    }

    [HttpDelete("storage-locations/{id}")]
    public async Task<IActionResult> DeleteStorageLocation(string id, CancellationToken ct)
    {
        bool deleted = await _repository.DeleteStorageLocationAsync(id, ct);
        if (!deleted)
        {
            return NotFound(new { error = "Storage location not found." });
        }
        return NoContent();
    }

    [HttpPost("scan")]
    public async Task<ActionResult<GameScanJobSnapshot>> StartScan(
        [FromBody] StartGameScanRequest request,
        CancellationToken ct)
    {
        string workstation = HttpContext.Items["AdminWorkstation"] as string ?? "ADMIN-PC";
        string ip = HttpContext.Items["AdminSourceIp"] as string ?? "127.0.0.1";

        GameScanJobSnapshot job = await _scanner.StartBackgroundScanAsync(request, workstation, ip, ct);
        return AcceptedAtAction(nameof(GetScanStatus), new { jobId = job.JobId }, job);
    }

    [HttpGet("scan/{jobId}")]
    public ActionResult<GameScanJobSnapshot> GetScanStatus(string jobId)
    {
        GameScanJobSnapshot? snapshot = _scanner.GetJobSnapshot(jobId);
        if (snapshot is null)
        {
            return NotFound(new { error = "Scan job not found." });
        }
        return Ok(snapshot);
    }

    [HttpPost("scan/{jobId}/cancel")]
    public ActionResult<object> CancelScan(string jobId)
    {
        bool cancelled = _scanner.CancelScan(jobId);
        if (!cancelled)
        {
            return NotFound(new { error = "Scan job not found." });
        }
        return Ok(new { cancelled = true, jobId });
    }

    [HttpPost("artwork/search")]
    public async Task<ActionResult<CoverSearchResponse>> SearchCoverArt(
        [FromBody] CoverSearchRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GameTitle) && string.IsNullOrWhiteSpace(request.ExternalAppId))
        {
            return BadRequest(new { error = "Game title or AppID is required to search artwork." });
        }

        CoverSearchResponse result = await _coverLookup.SearchArtworkAndMetadataAsync(
            request.GameTitle,
            request.ExternalAppId,
            request.Platform,
            ct);

        return Ok(result);
    }

    [HttpPost("artwork/upload")]
    public async Task<ActionResult<object>> UploadArtwork([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > _options.MaxImageUploadBytes)
        {
            return BadRequest(new { error = "Cover artwork must be non-empty and under 10 MB." });
        }

        string ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (ext is not (".jpg" or ".jpeg" or ".png" or ".webp"))
        {
            return BadRequest(new { error = "Only JPG, PNG, and WebP artwork images are allowed." });
        }

        byte[] header = new byte[64];
        await using Stream readStream = file.OpenReadStream();
        int read = await readStream.ReadAsync(header.AsMemory(0, header.Length), ct);
        AssetValidationResult validation = AssetUploadValidator.ValidateAsset(file.FileName, header.AsSpan(0, read), file.Length);
        if (!validation.IsValid || validation.MediaKind != "image")
        {
            return BadRequest(new { error = validation.Error });
        }

        string baseDir = Path.GetFullPath(_options.CoversDirectory);
        Directory.CreateDirectory(baseDir);

        string safeName = AssetUploadValidator.GenerateUniqueAssetFilename(file.FileName);
        string fullPath = Path.GetFullPath(Path.Combine(baseDir, safeName));
        if (!fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Invalid destination path." });
        }

        readStream.Position = 0;
        await using (var fs = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            await readStream.CopyToAsync(fs, ct);
        }

        return Ok(new { coverImagePath = $"/covers/{safeName}" });
    }

    [HttpPost("import")]
    public async Task<ActionResult<ImportDetectedGamesResponse>> ImportDetectedGames(
        [FromBody] ImportDetectedGamesRequest request,
        CancellationToken ct)
    {
        if (request.Items.Count == 0)
        {
            return BadRequest(new { error = "Select at least one detected game to import." });
        }

        string workstation = HttpContext.Items["AdminWorkstation"] as string ?? "ADMIN-PC";
        string ip = HttpContext.Items["AdminSourceIp"] as string ?? "127.0.0.1";

        ImportDetectedGamesResponse result = await _scanner.ImportConfirmedGamesAsync(request, workstation, ip, ct);
        return Ok(result);
    }
}
