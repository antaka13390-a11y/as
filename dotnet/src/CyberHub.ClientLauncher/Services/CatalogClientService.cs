using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CyberHub.Shared.Models;

namespace CyberHub.ClientLauncher.Services;

public sealed record CatalogFetchResult(
    PublishedCatalogSnapshot Snapshot,
    string SourceLabel,
    bool IsOfflineFallback,
    string? WarningKey);

public sealed class CatalogClientService : IDisposable
{
    private static readonly HashSet<string> AllowedCoverExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private readonly ClientConfig _config;
    private readonly HttpClient _httpClient;
    private readonly string _localCachePath;
    private readonly string _localCoversDir;

    public CatalogClientService(ClientConfig config)
    {
        _config = config;
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberHub");
        Directory.CreateDirectory(appData);
        _localCachePath = Path.Combine(appData, "catalog-cache.json");
        _localCoversDir = Path.Combine(appData, "covers");
        Directory.CreateDirectory(_localCoversDir);

        var handler = new HttpClientHandler();
        if (_config.AllowSelfSignedLanCert)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_config.ServerBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.RequestTimeoutSeconds, 3, 30))
        };
    }

    public async Task<CatalogFetchResult> LoadCatalogAsync(CancellationToken ct = default)
    {
        try
        {
            PublishedCatalogSnapshot? snapshot = await _httpClient.GetFromJsonAsync<PublishedCatalogSnapshot>("api/catalog", ct);
            if (snapshot is not null)
            {
                await CacheGameArtworkAsync(snapshot.Games, preferSmbShare: false, ct);
                await SaveLocalCacheAsync(snapshot, ct);
                return new CatalogFetchResult(snapshot, "HTTPS", false, null);
            }
        }
        catch (Exception)
        {
            // Fall through to read-only SMB share
        }

        try
        {
            if (File.Exists(_config.SmbReadOnlyCatalogPath))
            {
                string json = await File.ReadAllTextAsync(_config.SmbReadOnlyCatalogPath, ct);
                PublishedCatalogSnapshot? snapshot = JsonSerializer.Deserialize<PublishedCatalogSnapshot>(json);
                if (snapshot is not null)
                {
                    await CacheGameArtworkAsync(snapshot.Games, preferSmbShare: true, ct);
                    await SaveLocalCacheAsync(snapshot, ct);
                    return new CatalogFetchResult(snapshot, "SMB Read-Only", true, "Offline.SmbFallback");
                }
            }
        }
        catch (Exception)
        {
            // Fall through to local station cache
        }

        if (File.Exists(_localCachePath))
        {
            try
            {
                string cachedJson = await File.ReadAllTextAsync(_localCachePath, ct);
                PublishedCatalogSnapshot? cached = JsonSerializer.Deserialize<PublishedCatalogSnapshot>(cachedJson);
                if (cached is not null)
                {
                    return new CatalogFetchResult(cached, "Local Cache", true, "Offline.LocalCache");
                }
            }
            catch (Exception)
            {
                // Ignore corrupt cache
            }
        }

        return new CatalogFetchResult(new PublishedCatalogSnapshot(), "Offline", true, "Offline.LocalCache");
    }

    public async Task<bool> VerifyStationRecoveryCodeAsync(string stationName, string recoveryCode, CancellationToken ct = default)
    {
        try
        {
            HttpResponseMessage resp = await _httpClient.PostAsJsonAsync(
                "api/catalog/verify-recovery",
                new VerifyRecoveryRequest { StationName = stationName, RecoveryCode = recoveryCode },
                ct);
            return resp.IsSuccessStatusCode;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public string ResolveImageUri(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        string? localCached = GetSafeCachedCoverPath(rawPath);
        if (localCached is not null && File.Exists(localCached))
        {
            return localCached;
        }

        if (rawPath.StartsWith("/", StringComparison.Ordinal))
        {
            return _config.ServerBaseUrl.TrimEnd('/') + rawPath;
        }

        return rawPath;
    }

    private async Task CacheGameArtworkAsync(IReadOnlyList<GameEntry> games, bool preferSmbShare, CancellationToken ct)
    {
        string? smbCoversDir = null;
        string? smbCatalogDir = Path.GetDirectoryName(_config.SmbReadOnlyCatalogPath);
        if (!string.IsNullOrWhiteSpace(smbCatalogDir))
        {
            smbCoversDir = Path.Combine(smbCatalogDir, "covers");
        }

        foreach (string rawPath in games
                     .SelectMany(g => new[] { g.CoverImagePath, g.HeroImagePath })
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (ct.IsCancellationRequested)
            {
                break;
            }

            string? targetFile = GetSafeCachedCoverPath(rawPath);
            // Unchanged unique filenames are skipped automatically to avoid duplicate downloads
            if (targetFile is null || File.Exists(targetFile))
            {
                continue;
            }

            if (rawPath.StartsWith("/covers/", StringComparison.OrdinalIgnoreCase))
            {
                bool cached = false;
                if (!preferSmbShare)
                {
                    try
                    {
                        byte[] data = await _httpClient.GetByteArrayAsync(rawPath.TrimStart('/'), ct);
                        if (data.Length > 0)
                        {
                            string temp = $"{targetFile}.{Guid.NewGuid():N}.tmp";
                            await File.WriteAllBytesAsync(temp, data, ct);
                            File.Move(temp, targetFile, overwrite: true);
                            cached = true;
                        }
                    }
                    catch (Exception)
                    {
                        // Fall back to SMB share copy below
                    }
                }

                if (!cached && !string.IsNullOrWhiteSpace(smbCoversDir))
                {
                    try
                    {
                        string smbSource = Path.Combine(smbCoversDir, Path.GetFileName(rawPath));
                        if (File.Exists(smbSource))
                        {
                            string temp = $"{targetFile}.{Guid.NewGuid():N}.tmp";
                            File.Copy(smbSource, temp, overwrite: true);
                            File.Move(temp, targetFile, overwrite: true);
                        }
                    }
                    catch (Exception)
                    {
                        // Non-fatal per image
                    }
                }
            }
        }
    }

    private string? GetSafeCachedCoverPath(string rawPath)
    {
        if (rawPath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        string fileName = Path.GetFileName(rawPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string ext = Path.GetExtension(fileName);
        if (!AllowedCoverExtensions.Contains(ext))
        {
            return null;
        }

        string baseDir = Path.GetFullPath(_localCoversDir);
        string full = Path.GetFullPath(Path.Combine(baseDir, fileName));
        return full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            ? full
            : null;
    }

    private async Task SaveLocalCacheAsync(PublishedCatalogSnapshot snapshot, CancellationToken ct)
    {
        try
        {
            string json = JsonSerializer.Serialize(snapshot);
            await File.WriteAllTextAsync(_localCachePath, json, ct);
        }
        catch (Exception)
        {
            // Non-fatal
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
