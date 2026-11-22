using System.Text.Json.Serialization;

namespace CyberHub.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LaunchType
{
    DirectExecutable = 0,
    Steam = 1,
    RiotClient = 2,
    ExternalLauncher = 3
}

public sealed class GameEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Action";
    public string Description { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public string CoverImagePath { get; set; } = string.Empty;
    public string HeroImagePath { get; set; } = string.Empty;
    public string StorageLocationId { get; set; } = "ccboot-primary";
    public string ExecutablePath { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public LaunchType LaunchType { get; set; } = LaunchType.DirectExecutable;
    public string ExternalAppId { get; set; } = string.Empty;
    public string ProcessNameToMonitor { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StorageLocation
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string UncRootPath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsReadOnlyForClients { get; set; } = true;
}

public sealed class PublishedCatalogSnapshot
{
    public long CatalogVersion { get; set; }
    public DateTimeOffset PublishedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public string ServerHostname { get; set; } = string.Empty;
    public List<StorageLocation> StorageLocations { get; set; } = new();
    public List<GameEntry> Games { get; set; } = new();
}

public sealed class AdminAuditLog
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string TargetGameId { get; set; } = string.Empty;
    public string TargetGameName { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public string SourceIp { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class AdminStatusResponse
{
    public bool IsConfigured { get; set; }
    public bool WorkstationAuthorized { get; set; }
    public string DetectedClientIp { get; set; } = string.Empty;
    public string DetectedWorkstation { get; set; } = string.Empty;
    public int TotalGames { get; set; }
    public int VisibleGames { get; set; }
    public string LastBackupUtc { get; set; } = string.Empty;
}

public sealed class AdminSetupRequest
{
    public string Password { get; set; } = string.Empty;
}

public sealed class AdminLoginRequest
{
    public string Password { get; set; } = string.Empty;
}

public sealed class AdminLoginResponse
{
    public string AccessToken { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
    public string Workstation { get; set; } = string.Empty;
}

public sealed class UpsertGameRequest
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Action";
    public string Description { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public string CoverImagePath { get; set; } = string.Empty;
    public string HeroImagePath { get; set; } = string.Empty;
    public string StorageLocationId { get; set; } = "ccboot-primary";
    public string ExecutablePath { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public LaunchType LaunchType { get; set; } = LaunchType.DirectExecutable;
    public string ExternalAppId { get; set; } = string.Empty;
    public string ProcessNameToMonitor { get; set; } = string.Empty;
    public bool IsVisible { get; set; } = true;
    public bool IsFeatured { get; set; }
    public int SortOrder { get; set; }
}

public sealed class BackupResult
{
    public string BackupFilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
