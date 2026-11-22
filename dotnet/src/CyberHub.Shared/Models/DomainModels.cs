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

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NetworkMode
{
    Online = 0,
    Offline = 1
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CategoryFilterType
{
    Custom = 0,
    AllGames = 1,
    OnlineGames = 2,
    OfflineGames = 3
}

public sealed class CategoryEntry
{
    public string Id { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string IconKey { get; set; } = "gamepad";
    public string BannerImagePath { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
    public bool IsSystem { get; set; }
    public CategoryFilterType FilterType { get; set; } = CategoryFilterType.Custom;
    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class GameEntry
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Action";
    public List<string> CategoryIds { get; set; } = new();
    public NetworkMode NetworkMode { get; set; } = NetworkMode.Online;
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
    public List<CategoryEntry> Categories { get; set; } = new();
    public List<GameEntry> Games { get; set; } = new();
    public PublishedThemePayload? PublishedTheme { get; set; }
}

public sealed class LocalGameSessionState
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public Guid GameId { get; set; }
    public string GameName { get; set; } = string.Empty;
    public string StationName { get; set; } = Environment.MachineName;
    public int? RootProcessId { get; set; }
    public List<int> TrackedProcessIds { get; set; } = new();
    public List<string> MonitoredProcessNames { get; set; } = new();
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public bool RequiresAdminRecovery { get; set; }
    public string FailureReason { get; set; } = string.Empty;
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
    public int TotalCategories { get; set; }
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
    public string Category { get; set; } = string.Empty;
    public List<string> CategoryIds { get; set; } = new();
    public NetworkMode NetworkMode { get; set; } = NetworkMode.Online;
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

public sealed class UpsertCategoryRequest
{
    public string NameEn { get; set; } = string.Empty;
    public string NameAr { get; set; } = string.Empty;
    public string IconKey { get; set; } = "gamepad";
    public string BannerImagePath { get; set; } = string.Empty;
    public int DisplayOrder { get; set; }
}

public sealed class RecoveryCodeResponse
{
    public string StationName { get; set; } = string.Empty;
    public string RecoveryCode { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAtUtc { get; set; }
}

public sealed class VerifyRecoveryRequest
{
    public string StationName { get; set; } = string.Empty;
    public string RecoveryCode { get; set; } = string.Empty;
}

public sealed class BackupResult
{
    public string BackupFilePath { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAtUtc { get; set; }
}
