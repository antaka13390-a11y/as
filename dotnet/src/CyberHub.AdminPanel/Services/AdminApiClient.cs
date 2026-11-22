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
    public int RequestTimeoutSeconds { get; set; } = 10;
}

public sealed class AdminApiClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private string _accessToken = string.Empty;

    public AdminApiClient()
    {
        AdminConfig config = LoadConfig();
        var handler = new HttpClientHandler();
        if (config.AllowSelfSignedLanCert)
        {
            handler.ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator;
        }

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(config.ServerBaseUrl.TrimEnd('/') + "/"),
            Timeout = TimeSpan.FromSeconds(Math.Clamp(config.RequestTimeoutSeconds, 3, 30))
        };
        _httpClient.DefaultRequestHeaders.Add("X-CyberHub-Workstation", Environment.MachineName);
    }

    public bool IsAuthenticated => !string.IsNullOrWhiteSpace(_accessToken);

    private static AdminConfig LoadConfig()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "admin-config.json");
        if (!File.Exists(path))
        {
            return new AdminConfig();
        }
        return JsonSerializer.Deserialize<AdminConfig>(File.ReadAllText(path)) ?? new AdminConfig();
    }

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

    public void Logout()
    {
        _accessToken = string.Empty;
        _httpClient.DefaultRequestHeaders.Authorization = null;
    }

    public async Task<List<GameEntry>> GetGamesAsync(CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync<List<GameEntry>>("api/admin/games", ct) ?? new();
    }

    public async Task<List<StorageLocation>> GetStorageLocationsAsync(CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync<List<StorageLocation>>("api/admin/games/storage-locations", ct) ?? new();
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

    public async Task<List<AdminAuditLog>> GetLogsAsync(CancellationToken ct = default)
    {
        return await _httpClient.GetFromJsonAsync<List<AdminAuditLog>>("api/admin/system/logs?limit=200", ct) ?? new();
    }

    public async Task<BackupResult> TriggerBackupAsync(CancellationToken ct = default)
    {
        HttpResponseMessage resp = await _httpClient.PostAsync("api/admin/system/backup", null, ct);
        await EnsureSuccessAsync(resp);
        return (await resp.Content.ReadFromJsonAsync<BackupResult>(ct))!;
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
