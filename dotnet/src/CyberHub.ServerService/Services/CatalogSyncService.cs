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
    private readonly SemaphoreSlim _publishLock = new(1, 1);

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
        List<CategoryEntry> categories = await _repository.GetCategoriesAsync(ct);
        List<GameEntry> visibleGames = await _repository.GetGamesAsync(includeHidden: false, ct);
        PublishedThemePayload publishedTheme = await _repository.GetPublishedThemePayloadAsync(ct);

        return new PublishedCatalogSnapshot
        {
            CatalogVersion = version,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            ServerHostname = Environment.MachineName,
            StorageLocations = locations,
            Categories = categories,
            Games = visibleGames,
            PublishedTheme = publishedTheme
        };
    }

    public async Task<PublishedThemePayload> BuildReadOnlyThemeAsync(CancellationToken ct = default)
    {
        return await _repository.GetPublishedThemePayloadAsync(ct);
    }

    public async Task PublishSnapshotToShareAsync(CancellationToken ct = default)
    {
        await _publishLock.WaitAsync(ct);
        try
        {
            PublishedCatalogSnapshot snapshot = await BuildReadOnlyCatalogAsync(ct);
            await WriteAtomicJsonAsync(_options.PublishedCatalogPath, snapshot, ct);
            if (snapshot.PublishedTheme is not null)
            {
                await WriteAtomicJsonAsync(_options.PublishedThemePath, snapshot.PublishedTheme, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to publish read-only JSON catalog snapshot to {Path}", _options.PublishedCatalogPath);
        }
        finally
        {
            _publishLock.Release();
        }
    }

    private static async Task WriteAtomicJsonAsync<T>(string targetPath, T value, CancellationToken ct)
    {
        string? dir = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tempFile = $"{targetPath}.{Guid.NewGuid():N}.tmp";
        string json = JsonSerializer.Serialize(value, JsonOptions);
        await File.WriteAllTextAsync(tempFile, json, ct);
        File.Move(tempFile, targetPath, overwrite: true);
    }
}
