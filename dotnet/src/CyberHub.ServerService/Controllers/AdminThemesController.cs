using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.ServerService.Services;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using CyberHub.Shared.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/admin/themes")]
public sealed class AdminThemesController : ControllerBase
{
    private readonly SqliteGameRepository _repository;
    private readonly CatalogSyncService _syncService;
    private readonly ServerOptions _options;

    public AdminThemesController(
        SqliteGameRepository repository,
        CatalogSyncService syncService,
        IOptions<ServerOptions> options)
    {
        _repository = repository;
        _syncService = syncService;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<ActionResult<AdminThemesSnapshotResponse>> GetThemesSnapshot(CancellationToken ct)
    {
        List<ThemeConfig> themes = await _repository.GetThemesAsync(ct);
        (string activeId, long publishedVersion, _) = await _repository.GetThemeMetadataAsync(ct);
        List<ThemeAssetMetadata> assets = await _repository.GetThemeAssetsAsync(ct);
        List<ThemeAuditLog> logs = await _repository.GetThemeAuditLogsAsync(200, ct);

        return Ok(new AdminThemesSnapshotResponse
        {
            ActiveThemeId = activeId,
            PublishedVersion = publishedVersion,
            Themes = themes,
            Assets = assets,
            AuditLogs = logs
        });
    }

    [HttpGet("presets")]
    public ActionResult<List<ThemeConfig>> GetBuiltInPresets()
    {
        return Ok(ThemePresetFactory.CreateBuiltInPresets());
    }

    [HttpPost]
    public async Task<ActionResult<ThemeConfig>> CreateTheme([FromBody] ThemeConfig request, CancellationToken ct)
    {
        (bool valid, string error) = UncPathValidator.ValidateThemeConfig(request);
        if (!valid)
        {
            return BadRequest(new { error });
        }

        (string workstation, string ip) = GetCaller();
        ThemeConfig created = await _repository.SaveThemeDraftAsync(request, isNew: true, workstation, ip, ct);
        return CreatedAtAction(nameof(GetThemesSnapshot), new { id = created.Id }, created);
    }

    [HttpPut("{id}")]
    public async Task<ActionResult<ThemeConfig>> SaveThemeDraft(string id, [FromBody] ThemeConfig request, CancellationToken ct)
    {
        request.Id = id;
        (bool valid, string error) = UncPathValidator.ValidateThemeConfig(request);
        if (!valid)
        {
            return BadRequest(new { error });
        }

        (string workstation, string ip) = GetCaller();
        try
        {
            ThemeConfig saved = await _repository.SaveThemeDraftAsync(request, isNew: false, workstation, ip, ct);
            return Ok(saved);
        }
        catch (KeyNotFoundException ex)
        {
            return NotFound(new { error = ex.Message });
        }
    }

    [HttpPost("{id}/duplicate")]
    public async Task<ActionResult<ThemeConfig>> DuplicateTheme(string id, [FromBody] DuplicateThemeRequest request, CancellationToken ct)
    {
        (string workstation, string ip) = GetCaller();
        ThemeConfig? copy = await _repository.DuplicateThemeAsync(id, request.NewName, workstation, ip, ct);
        if (copy is null)
        {
            return NotFound(new { error = "Source theme not found." });
        }

        return Ok(copy);
    }

    [HttpPatch("{id}/rename")]
    public async Task<ActionResult<ThemeConfig>> RenameTheme(string id, [FromBody] RenameThemeRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.NewName) || request.NewName.Trim().Length > 64)
        {
            return BadRequest(new { error = "Theme name is required and must be 64 characters or fewer." });
        }

        (string workstation, string ip) = GetCaller();
        ThemeConfig? renamed = await _repository.RenameThemeAsync(id, request.NewName, workstation, ip, ct);
        if (renamed is null)
        {
            return NotFound(new { error = "Theme not found." });
        }

        return Ok(renamed);
    }

    [HttpPost("{id}/set-default")]
    public async Task<ActionResult<ThemeConfig>> SetDefaultTheme(string id, CancellationToken ct)
    {
        (string workstation, string ip) = GetCaller();
        ThemeConfig? updated = await _repository.SetDefaultThemeAsync(id, workstation, ip, ct);
        if (updated is null)
        {
            return NotFound(new { error = "Theme not found." });
        }

        return Ok(updated);
    }

    [HttpPost("{id}/publish")]
    public async Task<ActionResult<PublishedThemePayload>> PublishTheme(string id, CancellationToken ct)
    {
        ThemeConfig? target = await _repository.GetThemeByIdAsync(id, ct);
        if (target is null)
        {
            return NotFound(new { error = "Theme not found." });
        }

        (bool valid, string error) = UncPathValidator.ValidateThemeConfig(target);
        if (!valid)
        {
            return BadRequest(new { error });
        }

        (string workstation, string ip) = GetCaller();
        PublishedThemePayload? payload = await _repository.PublishThemeAsync(id, workstation, ip, ct);
        if (payload is null)
        {
            return NotFound(new { error = "Theme not found." });
        }

        await _syncService.PublishSnapshotToShareAsync(ct);
        return Ok(payload);
    }

    [HttpPost("restore-default")]
    public async Task<ActionResult<PublishedThemePayload>> RestoreDefaultTheme(CancellationToken ct)
    {
        (string workstation, string ip) = GetCaller();
        PublishedThemePayload payload = await _repository.RestoreDefaultThemeAsync(workstation, ip, ct);
        await _syncService.PublishSnapshotToShareAsync(ct);
        return Ok(payload);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTheme(string id, CancellationToken ct)
    {
        (string workstation, string ip) = GetCaller();
        (bool deleted, string error) = await _repository.DeleteThemeSafelyAsync(id, workstation, ip, ct);
        if (!deleted)
        {
            return BadRequest(new { error });
        }

        return NoContent();
    }

    [HttpPost("assets")]
    [RequestSizeLimit(55 * 1024 * 1024)]
    public async Task<ActionResult<ThemeAssetMetadata>> UploadThemeAsset([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
        {
            return BadRequest(new { error = "Uploaded file is empty." });
        }

        byte[] header = new byte[64];
        await using (Stream readStream = file.OpenReadStream())
        {
            int read = await readStream.ReadAsync(header.AsMemory(0, header.Length), ct);
            AssetValidationResult validation = AssetUploadValidator.ValidateAsset(file.FileName, header.AsSpan(0, read), file.Length);
            if (!validation.IsValid)
            {
                return BadRequest(new { error = validation.Error });
            }

            string baseDir = Path.GetFullPath(_options.ThemeAssetsDirectory);
            Directory.CreateDirectory(baseDir);

            string safeFileName = AssetUploadValidator.GenerateUniqueAssetFilename(file.FileName);
            string fullPath = Path.GetFullPath(Path.Combine(baseDir, safeFileName));
            if (!fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Invalid asset storage destination." });
            }

            readStream.Position = 0;
            await using (var dest = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await readStream.CopyToAsync(dest, ct);
            }

            var metadata = new ThemeAssetMetadata
            {
                Id = Guid.NewGuid().ToString("N"),
                FileName = safeFileName,
                OriginalFileName = Path.GetFileName(file.FileName),
                MediaKind = validation.MediaKind,
                ContentType = validation.ContentType,
                SizeBytes = file.Length,
                RelativeUrl = $"/theme-assets/{safeFileName}",
                UploadedAtUtc = DateTimeOffset.UtcNow
            };

            (string workstation, string ip) = GetCaller();
            await _repository.RecordThemeAssetAsync(metadata, workstation, ip, ct);
            return Ok(metadata);
        }
    }

    private (string Workstation, string SourceIp) GetCaller()
    {
        string workstation = HttpContext.Items["AdminWorkstation"] as string ?? "ADMIN-PC";
        string ip = HttpContext.Items["AdminSourceIp"] as string ?? "127.0.0.1";
        return (workstation, ip);
    }
}
