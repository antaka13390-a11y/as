using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.ServerService.Services;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/admin/games")]
public sealed class AdminGamesController : ControllerBase
{
    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private readonly SqliteGameRepository _repository;
    private readonly CatalogSyncService _syncService;
    private readonly ServerOptions _options;

    public AdminGamesController(
        SqliteGameRepository repository,
        CatalogSyncService syncService,
        IOptions<ServerOptions> options)
    {
        _repository = repository;
        _syncService = syncService;
        _options = options.Value;
    }

    [HttpGet]
    public async Task<ActionResult<List<GameEntry>>> GetAllGames(CancellationToken ct)
    {
        return Ok(await _repository.GetGamesAsync(includeHidden: true, ct));
    }

    [HttpGet("storage-locations")]
    public async Task<ActionResult<List<StorageLocation>>> GetStorageLocations(CancellationToken ct)
    {
        return Ok(await _repository.GetStorageLocationsAsync(ct));
    }

    [HttpPost]
    public async Task<ActionResult<GameEntry>> CreateGame([FromBody] UpsertGameRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { error = "Game name is required." });
        }

        (bool validPath, string pathError) = UncPathValidator.ValidateGamePath(
            request.LaunchType,
            request.ExecutablePath,
            request.ExternalAppId,
            request.LaunchArguments);

        if (!validPath)
        {
            return BadRequest(new { error = pathError });
        }

        if (!UncPathValidator.IsSafeCoverImagePath(request.CoverImagePath))
        {
            return BadRequest(new { error = "Cover image path is invalid." });
        }

        var game = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Category = string.IsNullOrWhiteSpace(request.Category) ? "Action" : request.Category.Trim(),
            Description = request.Description.Trim(),
            Developer = request.Developer.Trim(),
            CoverImagePath = request.CoverImagePath.Trim(),
            HeroImagePath = request.HeroImagePath.Trim(),
            StorageLocationId = string.IsNullOrWhiteSpace(request.StorageLocationId) ? "ccboot-primary" : request.StorageLocationId.Trim(),
            ExecutablePath = request.ExecutablePath.Trim(),
            LaunchArguments = request.LaunchArguments.Trim(),
            LaunchType = request.LaunchType,
            ExternalAppId = request.ExternalAppId.Trim(),
            ProcessNameToMonitor = request.ProcessNameToMonitor.Trim(),
            IsVisible = request.IsVisible,
            IsFeatured = request.IsFeatured,
            SortOrder = request.SortOrder,
            CreatedAtUtc = DateTimeOffset.UtcNow,
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        await _repository.UpsertGameAsync(game, isNew: true, ct);
        await _syncService.PublishSnapshotToShareAsync(ct);

        await RecordLogAsync("GAME_ADDED", game.Id.ToString(), game.Name, $"Added via {game.LaunchType} ({game.ExecutablePath})", ct);
        return CreatedAtAction(nameof(GetAllGames), new { id = game.Id }, game);
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<GameEntry>> UpdateGame(Guid id, [FromBody] UpsertGameRequest request, CancellationToken ct)
    {
        GameEntry? existing = await _repository.GetGameByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(new { error = "Game record not found." });
        }

        (bool validPath, string pathError) = UncPathValidator.ValidateGamePath(
            request.LaunchType,
            request.ExecutablePath,
            request.ExternalAppId,
            request.LaunchArguments);

        if (!validPath)
        {
            return BadRequest(new { error = pathError });
        }

        existing.Name = request.Name.Trim();
        existing.Category = string.IsNullOrWhiteSpace(request.Category) ? "Action" : request.Category.Trim();
        existing.Description = request.Description.Trim();
        existing.Developer = request.Developer.Trim();
        existing.CoverImagePath = request.CoverImagePath.Trim();
        existing.HeroImagePath = request.HeroImagePath.Trim();
        existing.StorageLocationId = string.IsNullOrWhiteSpace(request.StorageLocationId) ? "ccboot-primary" : request.StorageLocationId.Trim();
        existing.ExecutablePath = request.ExecutablePath.Trim();
        existing.LaunchArguments = request.LaunchArguments.Trim();
        existing.LaunchType = request.LaunchType;
        existing.ExternalAppId = request.ExternalAppId.Trim();
        existing.ProcessNameToMonitor = request.ProcessNameToMonitor.Trim();
        existing.IsVisible = request.IsVisible;
        existing.IsFeatured = request.IsFeatured;
        existing.SortOrder = request.SortOrder;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _repository.UpsertGameAsync(existing, isNew: false, ct);
        await _syncService.PublishSnapshotToShareAsync(ct);

        await RecordLogAsync("GAME_UPDATED", existing.Id.ToString(), existing.Name, $"Updated game config (Visible={existing.IsVisible})", ct);
        return Ok(existing);
    }

    [HttpPatch("{id:guid}/visibility")]
    public async Task<ActionResult<GameEntry>> ToggleVisibility(Guid id, [FromQuery] bool visible, CancellationToken ct)
    {
        GameEntry? existing = await _repository.GetGameByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(new { error = "Game record not found." });
        }

        existing.IsVisible = visible;
        existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await _repository.UpsertGameAsync(existing, isNew: false, ct);
        await _syncService.PublishSnapshotToShareAsync(ct);

        await RecordLogAsync(visible ? "GAME_SHOWN" : "GAME_HIDDEN", existing.Id.ToString(), existing.Name, $"Visibility set to {visible}", ct);
        return Ok(existing);
    }

    /// <summary>
    /// Deletes ONLY the game record from the SQLite database. Never deletes game folders or executables on disk.
    /// </summary>
    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> RemoveFromLibrary(Guid id, CancellationToken ct)
    {
        GameEntry? existing = await _repository.GetGameByIdAsync(id, ct);
        if (existing is null)
        {
            return NotFound(new { error = "Game record not found." });
        }

        await _repository.DeleteGameRecordOnlyAsync(id, ct);
        await _syncService.PublishSnapshotToShareAsync(ct);

        await RecordLogAsync("GAME_RECORD_REMOVED", existing.Id.ToString(), existing.Name, "Removed library record only; game files on storage share untouched.", ct);
        return NoContent();
    }

    [HttpPost("covers")]
    public async Task<ActionResult<object>> UploadCover([FromForm] IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0 || file.Length > _options.MaxCoverUploadBytes)
        {
            return BadRequest(new { error = "Cover image must be non-empty and under 5 MB." });
        }

        string ext = Path.GetExtension(file.FileName);
        if (!AllowedImageExtensions.Contains(ext))
        {
            return BadRequest(new { error = "Only PNG, JPG, JPEG, and WEBP cover images are allowed." });
        }

        string baseDir = Path.GetFullPath(_options.CoversDirectory);
        Directory.CreateDirectory(baseDir);

        string safeName = $"{Guid.NewGuid():N}{ext.ToLowerInvariant()}";
        string fullPath = Path.GetFullPath(Path.Combine(baseDir, safeName));
        if (!fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new { error = "Invalid destination path." });
        }

        await using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await file.CopyToAsync(stream, ct);

        await RecordLogAsync("COVER_UPLOADED", string.Empty, safeName, $"Uploaded cover artwork ({file.Length} bytes)", ct);
        return Ok(new { coverImagePath = $"/covers/{safeName}" });
    }

    private Task RecordLogAsync(string action, string gameId, string gameName, string details, CancellationToken ct)
    {
        string workstation = HttpContext.Items["AdminWorkstation"] as string ?? "ADMIN-PC";
        string ip = HttpContext.Items["AdminSourceIp"] as string ?? "127.0.0.1";

        return _repository.AppendAuditLogAsync(new AdminAuditLog
        {
            Action = action,
            TargetGameId = gameId,
            TargetGameName = gameName,
            WorkstationName = workstation,
            SourceIp = ip,
            Details = details
        }, ct);
    }
}
