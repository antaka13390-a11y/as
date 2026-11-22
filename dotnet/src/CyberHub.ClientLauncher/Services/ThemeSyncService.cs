using System.IO;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using CyberHub.Shared.Services;

namespace CyberHub.ClientLauncher.Services;

public sealed record ThemeSyncResult(
    PublishedThemePayload Payload,
    string SourceLabel,
    bool IsOfflineFallback,
    bool SyncFailedRetainedPrevious,
    Dictionary<string, string> LocalAssetMap);

public sealed class ThemeSyncService : IDisposable
{
    private static readonly HashSet<string> AllowedLocalAssetExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".ico", ".mp4"
    };

    private readonly ClientConfig _config;
    private readonly HttpClient _httpClient;
    private readonly string _localCacheFile;
    private readonly string _localAssetsDir;
    private readonly SemaphoreSlim _syncLock = new(1, 1);

    private PublishedThemePayload _lastValidPayload;
    private Dictionary<string, string> _localAssetMap = new(StringComparer.OrdinalIgnoreCase);

    public event EventHandler<ThemeSyncResult>? ThemeUpdated;

    public ThemeSyncService(
        ClientConfig config,
        HttpMessageHandler? customHandler = null,
        string? customCacheRoot = null)
    {
        _config = config;

        string root = string.IsNullOrWhiteSpace(customCacheRoot)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CyberHub")
            : customCacheRoot;
        Directory.CreateDirectory(root);

        _localCacheFile = Path.Combine(root, "theme-cache.json");
        _localAssetsDir = Path.Combine(root, "theme-assets");
        Directory.CreateDirectory(_localAssetsDir);

        HttpMessageHandler handler = customHandler ?? CreateDefaultHandler(config.AllowSelfSignedLanCert);
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_config.ServerBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.RequestTimeoutSeconds, 3, 30))
        };

        _lastValidPayload = LoadCachedOrDefault();
    }

    public PublishedThemePayload CurrentPayload => _lastValidPayload;
    public IReadOnlyDictionary<string, string> LocalAssetMap => _localAssetMap;

    public ThemeSyncResult GetInitialCachedTheme()
    {
        RebuildLocalAssetMap(_lastValidPayload.Theme);
        return new ThemeSyncResult(_lastValidPayload, "Local Cache", true, false, new Dictionary<string, string>(_localAssetMap));
    }

    public async Task<ThemeSyncResult> SyncPublishedThemeAsync(CancellationToken ct = default)
    {
        await _syncLock.WaitAsync(ct);
        try
        {
            long previousVersion = _lastValidPayload.PublishedVersion;
            DateTimeOffset previousUpdated = _lastValidPayload.Theme.UpdatedAtUtc;

            PublishedThemePayload? fetched = null;
            string source = "Local Cache";
            bool offline = false;

            try
            {
                PublishedThemePayload? remote = await _httpClient.GetFromJsonAsync<PublishedThemePayload>("api/catalog/theme", ct);
                if (remote is not null && UncPathValidator.ValidateThemeConfig(remote.Theme).IsValid)
                {
                    fetched = remote;
                    source = "HTTPS";
                }
            }
            catch (Exception)
            {
                // Fall back to SMB share
            }

            if (fetched is null)
            {
                try
                {
                    if (!string.IsNullOrWhiteSpace(_config.SmbReadOnlyThemePath) && File.Exists(_config.SmbReadOnlyThemePath))
                    {
                        string smbJson = await File.ReadAllTextAsync(_config.SmbReadOnlyThemePath, ct);
                        PublishedThemePayload? smbPayload = JsonSerializer.Deserialize<PublishedThemePayload>(smbJson);
                        if (smbPayload is not null && UncPathValidator.ValidateThemeConfig(smbPayload.Theme).IsValid)
                        {
                            fetched = smbPayload;
                            source = "SMB Read-Only";
                            offline = true;
                        }
                    }
                }
                catch (Exception)
                {
                    // Fall back cleanly to previous valid theme
                }
            }

            if (fetched is null)
            {
                RebuildLocalAssetMap(_lastValidPayload.Theme);
                return new ThemeSyncResult(
                    _lastValidPayload,
                    source,
                    true,
                    true,
                    new Dictionary<string, string>(_localAssetMap));
            }

            await CacheReferencedAssetsAsync(fetched.Theme, offline, ct);
            await SaveLocalCacheAsync(fetched, ct);
            _lastValidPayload = fetched;
            RebuildLocalAssetMap(fetched.Theme);

            var result = new ThemeSyncResult(
                fetched,
                source,
                offline,
                false,
                new Dictionary<string, string>(_localAssetMap));

            if (fetched.PublishedVersion != previousVersion || fetched.Theme.UpdatedAtUtc != previousUpdated)
            {
                ThemeUpdated?.Invoke(this, result);
            }

            return result;
        }
        finally
        {
            _syncLock.Release();
        }
    }

    public string ResolveAssetUri(string? rawAssetPath)
    {
        if (string.IsNullOrWhiteSpace(rawAssetPath))
        {
            return string.Empty;
        }

        if (_localAssetMap.TryGetValue(rawAssetPath, out string? localPath) && File.Exists(localPath))
        {
            return localPath;
        }

        if (rawAssetPath.StartsWith("/", StringComparison.Ordinal))
        {
            return _config.ServerBaseUrl.TrimEnd('/') + rawAssetPath;
        }

        return rawAssetPath;
    }

    private async Task CacheReferencedAssetsAsync(ThemeConfig theme, bool preferSmbShare, CancellationToken ct)
    {
        string? smbAssetsDir = null;
        if (!string.IsNullOrWhiteSpace(_config.SmbReadOnlyThemePath))
        {
            string? smbRoot = Path.GetDirectoryName(_config.SmbReadOnlyThemePath);
            if (!string.IsNullOrWhiteSpace(smbRoot))
            {
                smbAssetsDir = Path.Combine(smbRoot, "theme-assets");
            }
        }

        string[] paths =
        {
            theme.BackgroundImageAssetPath,
            theme.BackgroundVideoAssetPath,
            theme.StaticFallbackImagePath,
            theme.LogoAssetPath,
            theme.AppIconAssetPath,
            theme.LoadingLogoAssetPath
        };

        foreach (string raw in paths.Where(p => !string.IsNullOrWhiteSpace(p)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            string? localTarget = GetSafeLocalCacheFilePath(raw);
            if (localTarget is null || File.Exists(localTarget))
            {
                continue;
            }

            if (raw.StartsWith("/theme-assets/", StringComparison.OrdinalIgnoreCase))
            {
                bool cached = false;
                if (!preferSmbShare)
                {
                    try
                    {
                        byte[] data = await _httpClient.GetByteArrayAsync(raw.TrimStart('/'), ct);
                        if (data.Length > 0)
                        {
                            string tempFile = $"{localTarget}.{Guid.NewGuid():N}.tmp";
                            await File.WriteAllBytesAsync(tempFile, data, ct);
                            File.Move(tempFile, localTarget, overwrite: true);
                            cached = true;
                        }
                    }
                    catch (Exception)
                    {
                        // Fall back to SMB copy below
                    }
                }

                if (!cached && !string.IsNullOrWhiteSpace(smbAssetsDir))
                {
                    try
                    {
                        string smbSource = Path.Combine(smbAssetsDir, Path.GetFileName(raw));
                        if (File.Exists(smbSource))
                        {
                            string tempFile = $"{localTarget}.{Guid.NewGuid():N}.tmp";
                            File.Copy(smbSource, tempFile, overwrite: true);
                            File.Move(tempFile, localTarget, overwrite: true);
                        }
                    }
                    catch (Exception)
                    {
                        // Preserve cache gracefully if SMB asset is temporarily unreachable
                    }
                }
            }
        }
    }

    private void RebuildLocalAssetMap(ThemeConfig theme)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string[] paths =
        {
            theme.BackgroundImageAssetPath,
            theme.BackgroundVideoAssetPath,
            theme.StaticFallbackImagePath,
            theme.LogoAssetPath,
            theme.AppIconAssetPath,
            theme.LoadingLogoAssetPath
        };

        foreach (string raw in paths.Where(p => !string.IsNullOrWhiteSpace(p)))
        {
            string? localTarget = GetSafeLocalCacheFilePath(raw);
            if (localTarget is not null && File.Exists(localTarget))
            {
                map[raw] = localTarget;
            }
        }

        _localAssetMap = map;
    }

    private string? GetSafeLocalCacheFilePath(string rawPath)
    {
        if (string.IsNullOrWhiteSpace(rawPath) || rawPath.Contains("..", StringComparison.Ordinal))
        {
            return null;
        }

        string fileName = Path.GetFileName(rawPath);
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        string ext = Path.GetExtension(fileName);
        if (!AllowedLocalAssetExtensions.Contains(ext))
        {
            return null;
        }

        string baseDir = Path.GetFullPath(_localAssetsDir);
        string full = Path.GetFullPath(Path.Combine(baseDir, fileName));
        if (!full.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return full;
    }

    private PublishedThemePayload LoadCachedOrDefault()
    {
        if (File.Exists(_localCacheFile))
        {
            try
            {
                string json = File.ReadAllText(_localCacheFile);
                PublishedThemePayload? cached = JsonSerializer.Deserialize<PublishedThemePayload>(json);
                if (cached is not null && UncPathValidator.ValidateThemeConfig(cached.Theme).IsValid)
                {
                    return cached;
                }
            }
            catch (Exception)
            {
                // Corrupt local cache falls back cleanly to default built-in preset
            }
        }

        return new PublishedThemePayload
        {
            PublishedVersion = 1,
            PublishedAtUtc = DateTimeOffset.UtcNow,
            Theme = ThemePresetFactory.CreateNeonPurple(isDefault: true, isPublished: true)
        };
    }

    private async Task SaveLocalCacheAsync(PublishedThemePayload payload, CancellationToken ct)
    {
        try
        {
            string tempFile = $"{_localCacheFile}.{Guid.NewGuid():N}.tmp";
            string json = JsonSerializer.Serialize(payload);
            await File.WriteAllTextAsync(tempFile, json, ct);
            File.Move(tempFile, _localCacheFile, overwrite: true);
        }
        catch (Exception)
        {
            // Non-fatal
        }
    }

    private static HttpMessageHandler CreateDefaultHandler(bool allowSelfSigned)
    {
        var handler = new HttpClientHandler();
        if (allowSelfSigned)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }
        return handler;
    }

    public void Dispose()
    {
        _syncLock.Dispose();
        _httpClient.Dispose();
    }
}
