using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using CyberHub.Shared.Models;

namespace CyberHub.AdminPanel.Services;

public sealed class AdminConfig
{
    public string ServerBaseUrl { get; set; } = "https://ccboot-srv.lan:8443";
    public bool AllowSelfSignedLanCert { get; set; } = true;
    public int RequestTimeoutSeconds { get; set; } = 15;
}

public sealed class AdminApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly AdminConfig _config;
    private string _accessToken = string.Empty;

    public AdminApiClient()
    {
        _config = LoadConfig();
        var handler = new HttpClientHandler();
        if (_config.AllowSelfSignedLanCert)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_config.ServerBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Clamp(_config.RequestTimeoutSeconds, 3, 60))
        };
        _httpClient.DefaultRequestHeaders.Add("X-CyberHub-Workstation", Environment.MachineName);
    }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);

    public string ResolveServerAssetUrl(string? rawPath)
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

    public void ClearSession()
    {
        _accessToken = string.Empty;
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    private static AdminConfig LoadConfig()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "admin-config.json");
        if (!File.Exists(path))
        {
            return new AdminConfig();
        }
        return JsonSerializer.Deserialize<AdminConfig>(File.ReadAllText(path)) ?? new AdminConfig();
    }

    // Auth & Security
    public async Task<AdminStatusResponse> GetStatusAsync(CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.GetAsync("api/admin/status", ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<AdminStatusResponse>(ct))!;
    }

    public async Task<AdminLoginResponse> SetupInitialAdminAsync(string password, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/setup", new AdminSetupRequest { Password = password }, ct);
        await EnsureSuccessAsync(resp);
        var login = (await resp.Content.ReadFromJsonAsync<AdminLoginResponse>(ct))!;
        SetBearerToken(login.AccessToken);
        return login;
    }

    public async Task<AdminLoginResponse> LoginAsync(string password, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/login", new AdminLoginRequest { Password = password }, ct);
        await EnsureSuccessAsync(resp);
        var login = (await resp.Content.ReadFromJsonAsync<AdminLoginResponse>(ct))!;
        SetBearerToken(login.AccessToken);
        return login;
    }

    public async Task<ChangePasswordResponse> ChangePasswordAsync(
        string currentPassword,
        string newPassword,
        string confirmNewPassword,
        CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/change-password", new ChangePasswordRequest
        {
            CurrentPassword = currentPassword,
            NewPassword = newPassword,
            ConfirmNewPassword = confirmNewPassword
        }, ct);

        string rawBody = await resp.Content.ReadAsStringAsync(ct);
        ChangePasswordResponse? body = null;
        if (!string.IsNullOrWhiteSpace(rawBody))
        {
            try
            {
                body = JsonSerializer.Deserialize<ChangePasswordResponse>(rawBody, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (JsonException)
            {
                // Fall back cleanly
            }
        }

        if (!resp.IsSuccessStatusCode)
        {
            if (!string.IsNullOrWhiteSpace(body?.Message))
            {
                throw new InvalidOperationException(body.Message);
            }

            try
            {
                using JsonDocument doc = JsonDocument.Parse(rawBody);
                if (doc.RootElement.TryGetProperty("error", out JsonElement err) &&
                    !string.IsNullOrWhiteSpace(err.GetString()))
                {
                    throw new InvalidOperationException(err.GetString());
                }
            }
            catch (JsonException)
            {
                // Fall through to HTTP status fallback
            }

            throw new InvalidOperationException($"Server returned HTTP {(int)resp.StatusCode}: {rawBody}");
        }

        if (body?.RequiresRelogin == true)
        {
            ClearSession();
        }

        return body ?? new ChangePasswordResponse { Success = true, RequiresRelogin = true };
    }

    public async Task LogoutAsync(CancellationToken ct = default)
    {
        try
        {
            await _httpClient.PostAsync("api/admin/logout", null, ct);
        }
        finally
        {
            ClearSession();
        }
    }

    // Games & Categories
    public async Task<List<GameEntry>> GetGamesAsync(CancellationToken ct = default) =>
        await _httpClient.GetFromJsonAsync<List<GameEntry>>("api/admin/games", ct) ?? new();

    public async Task<List<StorageLocation>> GetStorageLocationsAsync(CancellationToken ct = default) =>
        await _httpClient.GetFromJsonAsync<List<StorageLocation>>("api/admin/scanner/storage-locations", ct) ?? new();

    public async Task<StorageLocation> SaveStorageLocationAsync(UpsertStorageLocationRequest request, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/scanner/storage-locations", request, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<StorageLocation>(ct))!;
    }

    public async Task DeleteStorageLocationAsync(string id, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.DeleteAsync($"api/admin/scanner/storage-locations/{Uri.EscapeDataString(id)}", ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task<List<CategoryEntry>> GetCategoriesAsync(CancellationToken ct = default) =>
        await _httpClient.GetFromJsonAsync<List<CategoryEntry>>("api/admin/categories", ct) ?? new();

    public async Task<CategoryEntry> CreateCategoryAsync(UpsertCategoryRequest request, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/categories", request, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<CategoryEntry>(ct))!;
    }

    public async Task<CategoryEntry> UpdateCategoryAsync(string id, UpsertCategoryRequest request, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PutAsJsonAsync($"api/admin/categories/{Uri.EscapeDataString(id)}", request, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<CategoryEntry>(ct))!;
    }

    public async Task DeleteCategorySafelyAsync(string id, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.DeleteAsync($"api/admin/categories/{Uri.EscapeDataString(id)}", ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task<GameEntry> CreateGameAsync(UpsertGameRequest request, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/games", request, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<GameEntry>(ct))!;
    }

    public async Task<GameEntry> UpdateGameAsync(Guid id, UpsertGameRequest request, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PutAsJsonAsync($"api/admin/games/{id}", request, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<GameEntry>(ct))!;
    }

    public async Task<GameEntry> SetVisibilityAsync(Guid id, bool visible, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PatchAsync($"api/admin/games/{id}/visibility?visible={visible.ToString().ToLowerInvariant()}", null, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<GameEntry>(ct))!;
    }

    public async Task RemoveGameRecordOnlyAsync(Guid id, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.DeleteAsync($"api/admin/games/{id}", ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task<string> UploadCoverAsync(string localFilePath, CancellationToken ct = default)
    {
        await using FileStream stream = File.OpenRead(localFilePath);
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        form.Add(fileContent, "file", Path.GetFileName(localFilePath));

        HttpResponseMessage resp = await _httpClient.PostAsync("api/admin/games/covers", form, ct);
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("coverImagePath").GetString() ?? string.Empty;
    }

    // Automatic Game Detection & Cover Art Manager (Part 11)
    public async Task<GameScanJobSnapshot> StartGameScanAsync(StartGameScanRequest request, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/scanner/scan", request, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<GameScanJobSnapshot>(ct))!;
    }

    public async Task<GameScanJobSnapshot> GetScanStatusAsync(string jobId, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.GetAsync($"api/admin/scanner/scan/{Uri.EscapeDataString(jobId)}", ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<GameScanJobSnapshot>(ct))!;
    }

    public async Task CancelGameScanAsync(string jobId, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsync($"api/admin/scanner/scan/{Uri.EscapeDataString(jobId)}/cancel", null, ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task<CoverSearchResponse> SearchCoverArtAsync(CoverSearchRequest request, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/scanner/artwork/search", request, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<CoverSearchResponse>(ct))!;
    }

    public async Task<string> UploadScannerArtworkAsync(string localFilePath, CancellationToken ct = default)
    {
        await using FileStream stream = File.OpenRead(localFilePath);
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        form.Add(fileContent, "file", Path.GetFileName(localFilePath));

        HttpResponseMessage resp = await _httpClient.PostAsync("api/admin/scanner/artwork/upload", form, ct);
        await EnsureSuccessAsync(resp);
        var json = await resp.Content.ReadFromJsonAsync<JsonElement>(ct);
        return json.GetProperty("coverImagePath").GetString() ?? string.Empty;
    }

    public async Task<ImportDetectedGamesResponse> ImportDetectedGamesAsync(ImportDetectedGamesRequest request, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/scanner/import", request, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ImportDetectedGamesResponse>(ct))!;
    }

    // Appearance & Themes
    public async Task<AdminThemesSnapshotResponse> GetThemesSnapshotAsync(CancellationToken ct = default) =>
        await _httpClient.GetFromJsonAsync<AdminThemesSnapshotResponse>("api/admin/themes", ct) ?? new();

    public async Task<ThemeConfig> CreateThemeAsync(ThemeConfig theme, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync("api/admin/themes", theme, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ThemeConfig>(ct))!;
    }

    public async Task<ThemeConfig> SaveThemeDraftAsync(string id, ThemeConfig theme, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PutAsJsonAsync($"api/admin/themes/{Uri.EscapeDataString(id)}", theme, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ThemeConfig>(ct))!;
    }

    public async Task<ThemeConfig> DuplicateThemeAsync(string id, string newName, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsJsonAsync($"api/admin/themes/{Uri.EscapeDataString(id)}/duplicate", new DuplicateThemeRequest { NewName = newName }, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ThemeConfig>(ct))!;
    }

    public async Task<ThemeConfig> RenameThemeAsync(string id, string newName, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PatchAsJsonAsync($"api/admin/themes/{Uri.EscapeDataString(id)}/rename", new RenameThemeRequest { NewName = newName }, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ThemeConfig>(ct))!;
    }

    public async Task SetDefaultThemeAsync(string id, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsync($"api/admin/themes/{Uri.EscapeDataString(id)}/set-default", null, ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task DeleteThemeAsync(string id, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.DeleteAsync($"api/admin/themes/{Uri.EscapeDataString(id)}", ct);
        await EnsureSuccessAsync(resp);
    }

    public async Task<PublishedThemePayload> PublishThemeAsync(string id, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsync($"api/admin/themes/{Uri.EscapeDataString(id)}/publish", null, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<PublishedThemePayload>(ct))!;
    }

    public async Task<PublishedThemePayload> RestoreDefaultThemeAsync(CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsync("api/admin/themes/restore-default", null, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<PublishedThemePayload>(ct))!;
    }

    public async Task<ThemeAssetMetadata> UploadThemeAssetAsync(string localFilePath, CancellationToken ct = default)
    {
        await using FileStream stream = File.OpenRead(localFilePath);
        using var form = new MultipartFormDataContent();
        using var fileContent = new StreamContent(stream);
        form.Add(fileContent, "file", Path.GetFileName(localFilePath));

        HttpResponseMessage resp = await _httpClient.PostAsync("api/admin/themes/assets", form, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<ThemeAssetMetadata>(ct))!;
    }

    // Logs, Backup & Recovery
    public async Task<List<AdminAuditLog>> GetLogsAsync(CancellationToken ct = default) =>
        await _httpClient.GetFromJsonAsync<List<AdminAuditLog>>("api/admin/system/logs?limit=200", ct) ?? new();

    public async Task<BackupResult> TriggerBackupAsync(CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsync("api/admin/system/backup", null, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BackupResult>(ct))!;
    }

    public async Task<RecoveryCodeResponse> IssueRecoveryCodeAsync(string stationName, CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsync(
            $"api/admin/system/recovery-code?stationName={Uri.EscapeDataString(stationName)}",
            null,
            ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<RecoveryCodeResponse>(ct))!;
    }

    private void SetBearerToken(string token)
    {
        _accessToken = token;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage resp)
    {
        if (resp.IsSuccessStatusCode)
        {
            return;
        }

        string body = await resp.Content.ReadAsStringAsync();
        try
        {
            using JsonDocument doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("error", out JsonElement err))
            {
                throw new InvalidOperationException(err.GetString() ?? $"HTTP {(int)resp.StatusCode}");
            }
        }
        catch (JsonException)
        {
            // Fall through
        }

        throw new InvalidOperationException($"Server returned HTTP {(int)resp.StatusCode}: {body}");
    }

    public void Dispose() => _httpClient.Dispose();
}
