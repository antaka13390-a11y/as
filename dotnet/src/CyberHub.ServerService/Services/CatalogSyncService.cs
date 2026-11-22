using System.Text.Json;
using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.Shared.Models;
using Microsoft.Extensions.Options;

namespace CyberHub.ServerService.Services;

public sealed class CatalogSyncService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly SqliteGameRepository _repository;
    private readonly ServerOptions _options;
    private readonly ILogger<CatalogSyncService> _logger;

    public CatalogSyncService(
        SqliteGameRepository repository,
        IOptions<ServerOptions> options,
        ILogger<CatalogSyncService> logger)
    {
        _repository = repository;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PublishedCatalogSnapshot> BuildReadOnlyCatalogAsync(CancellationToken ct = default)
    {
        (long version, _) = await _repository.GetCatalogMetadataAsync(ct);
        List<StorageLocation> locations = await _repository.GetStorageLocationsAsync(ct);
        List<GameEntry> visibleGames = await _repository.GetGamesAsync(includeHidden: false, ct);

        return new PublishedCatalogSnapshot
        {
            CatalogVersion = version,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            ServerHostname = Environment.MachineName,
            StorageLocations = locations,
            Games = visibleGames
        };
    }

    public async Task PublishSnapshotToShareAsync(CancellationToken ct = default)
    {
        try
        {
            PublishedCatalogSnapshot snapshot = await BuildReadOnlyCatalogAsync(ct);
            string? dir = Path.GetDirectoryName(_options.PublishedCatalogPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            string tempFile = _options.PublishedCatalogPath + ".tmp";
            string json = JsonSerializer.Serialize(snapshot, JsonOptions);
            await File.WriteAllTextAsync(tempFile, json, ct);
            File.Move(tempFile, _options.PublishedCatalogPath, overwrite: true);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish read-only JSON catalog snapshot to {Path}", _options.PublishedCatalogPath);
        }
    }
}
