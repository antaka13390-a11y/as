using System.IO;
using System.Text.Json;

namespace CyberHub.ClientLauncher.Services;

public sealed class PlayerPreferences
{
    public HashSet<Guid> FavoriteGameIds { get; set; } = new();
    public List<RecentPlayEntry> RecentlyPlayed { get; set; } = new();
}

public sealed class RecentPlayEntry
{
    public Guid GameId { get; set; }
    public DateTimeOffset LastPlayedUtc { get; set; }
}

public sealed class LocalPreferencesService
{
    private readonly string _prefsPath;

    public LocalPreferencesService()
    {
        string dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberHub");
        Directory.CreateDirectory(dir);
        _prefsPath = Path.Combine(dir, "player-prefs.json");
    }

    public PlayerPreferences Load()
    {
        if (!File.Exists(_prefsPath))
        {
            return new PlayerPreferences();
        }

        try
        {
            string json = File.ReadAllText(_prefsPath);
            return JsonSerializer.Deserialize<PlayerPreferences>(json) ?? new PlayerPreferences();
        }
        catch (Exception)
        {
            return new PlayerPreferences();
        }
    }

    public void Save(PlayerPreferences prefs)
    {
        string json = JsonSerializer.Serialize(prefs, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_prefsPath, json);
    }
}
