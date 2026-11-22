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
    string? WarningMessage);

public sealed class CatalogClientService : IDisposable
{
    private readonly ClientConfig _config;
    private readonly HttpClient _httpClient;
    private readonly string _localCachePath;

    public CatalogClientService(ClientConfig config)
    {
        _config = config;
        string appData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberHub");
        Directory.CreateDirectory(appData);
        _localCachePath = Path.Combine(appData, "catalog-cache.json");

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
        // 1. Primary: Read-only HTTPS ServerService endpoint
        try
        {
            PublishedCatalogSnapshot? snapshot = await _httpClient.GetFromJsonAsync<PublishedCatalogSnapshot>("api/catalog", ct);
            if (snapshot is not null)
            {
                await SaveLocalCacheAsync(snapshot, ct);
                return new CatalogFetchResult(snapshot, "Central Server (HTTPS)", false, null);
            }
        }
        catch (Exception)
        {
            // Fall through to read-only SMB share
        }

        // 2. Secondary: Read-only SMB snapshot share (\\CCBOOT-SRV\CyberHubReadOnly$\catalog.json)
        try
        {
            if (File.Exists(_config.SmbReadOnlyCatalogPath))
            {
                string json = await File.ReadAllTextAsync(_config.SmbReadOnlyCatalogPath, ct);
                PublishedCatalogSnapshot? snapshot = JsonSerializer.Deserialize<PublishedCatalogSnapshot>(json);
                if (snapshot is not null)
                {
                    await SaveLocalCacheAsync(snapshot, ct);
                    return new CatalogFetchResult(
                        snapshot,
                        "Read-Only SMB Share",
                        true,
                        "Central API unreachable; loaded read-only published SMB catalog.");
                }
            }
        }
        catch (Exception)
        {
            // Fall through to local station cache
        }

        // 3. Tertiary: Local station offline cache
        if (File.Exists(_localCachePath))
        {
            try
            {
                string cachedJson = await File.ReadAllTextAsync(_localCachePath, ct);
                PublishedCatalogSnapshot? cached = JsonSerializer.Deserialize<PublishedCatalogSnapshot>(cachedJson);
                if (cached is not null)
                {
                    return new CatalogFetchResult(
                        cached,
                        "Offline Station Cache",
                        true,
                        "Server offline. Showing last synchronized catalog from local cache.");
                }
            }
            catch (Exception)
            {
                // Ignore corrupt cache
            }
        }

        return new CatalogFetchResult(
            new PublishedCatalogSnapshot(),
            "Offline (Empty)",
            true,
            "Unable to reach CyberHub ServerService or read-only SMB catalog. Verify LAN connection.");
    }

    public string ResolveImageUri(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath))
        {
            return string.Empty;
        }

        if (rawPath.StartsWith("/", StringComparison.Ordinal))
        {
            return _config.ServerBaseUrl.TrimEnd('/') + rawPath;
        }

        return rawPath;
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
            // Non-fatal cache write error
        }
    }

    public void Dispose() => _httpClient.Dispose();
}
