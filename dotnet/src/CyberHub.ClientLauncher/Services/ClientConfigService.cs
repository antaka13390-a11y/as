using System.IO;
using System.Text.Json;

namespace CyberHub.ClientLauncher.Services;

public sealed class ClientConfig
{
    public string ServerBaseUrl { get; set; } = "https://ccboot-srv.lan:8443";
    public string SmbReadOnlyCatalogPath { get; set; } = @"\\CCBOOT-SRV\CyberHubReadOnly$\catalog.json";
    public string SteamExecutablePath { get; set; } = @"C:\Program Files (x86)\Steam\steam.exe";
    public string RiotClientPath { get; set; } = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
    public int RequestTimeoutSeconds { get; set; } = 8;
    public bool AllowSelfSignedLanCert { get; set; } = true;
}

public static class ClientConfigLoader
{
    public static ClientConfig Load()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "client-config.json");
        if (!File.Exists(path))
        {
            return new ClientConfig();
        }

        string json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ClientConfig>(json) ?? new ClientConfig();
    }
}
