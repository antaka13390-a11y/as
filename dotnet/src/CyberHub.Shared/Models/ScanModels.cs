using System.Text.Json.Serialization;

namespace CyberHub.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ScanConfidence
{
    Low = 0,
    Medium = 1,
    High = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum GameScanJobStatus
{
    Idle = 0,
    Running = 1,
    Completed = 2,
    Cancelled = 3,
    Failed = 4
}

public sealed class UpsertStorageLocationRequest
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string UncRootPath { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsReadOnlyForClients { get; set; } = true;
}

public sealed class StartGameScanRequest
{
    public List<string> StorageLocationIds { get; set; } = new();
    public List<string> CustomPaths { get; set; } = new();
    public int MaxDepth { get; set; } = 4;
    public bool IncludeSteamManifests { get; set; } = true;
    public bool IncludeRiotMetadata { get; set; } = true;
    public bool SearchCoverArt { get; set; } = true;
}

public sealed class ArtworkCandidate
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string SourceName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string HeroUrl { get; set; } = string.Empty;
    public int Width { get; set; }
    public int Height { get; set; }
    public ScanConfidence Confidence { get; set; } = ScanConfidence.Medium;
}

public sealed class DetectedGameMetadata
{
    public string OfficialTitle { get; set; } = string.Empty;
    public string ShortDescription { get; set; } = string.Empty;
    public string ReleaseYear { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public string Platform { get; set; } = "PC";
    public string HeroImageUrl { get; set; } = string.Empty;
}

public sealed class DetectedGameCandidate
{
    public string CandidateId { get; set; } = Guid.NewGuid().ToString("N");
    public string DetectedName { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
    public string ExecutablePath { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public LaunchType LaunchType { get; set; } = LaunchType.DirectExecutable;
    public string ExternalAppId { get; set; } = string.Empty;
    public string ProcessNameToMonitor { get; set; } = string.Empty;
    public string StorageLocationId { get; set; } = "ccboot-primary";
    public string SourceFolder { get; set; } = string.Empty;
    public ScanConfidence Confidence { get; set; } = ScanConfidence.Medium;
    public int ConfidenceScore { get; set; } = 60;
    public string DetectionSource { get; set; } = string.Empty;
    public bool IsDuplicate { get; set; }
    public string DuplicateReason { get; set; } = string.Empty;
    public Guid? DuplicateExistingGameId { get; set; }
    public string DuplicateExistingGameName { get; set; } = string.Empty;
    public string SuggestedCoverUrl { get; set; } = string.Empty;
    public string SelectedCoverUrl { get; set; } = string.Empty;
    public List<ArtworkCandidate> ArtworkCandidates { get; set; } = new();
    public DetectedGameMetadata Metadata { get; set; } = new();
    public bool IsSelected { get; set; } = true;
    public bool IsIgnored { get; set; }
}

public sealed class GameScanJobSnapshot
{
    public string JobId { get; set; } = Guid.NewGuid().ToString("N");
    public GameScanJobStatus Status { get; set; } = GameScanJobStatus.Idle;
    public int ScannedDirectories { get; set; }
    public int ScannedFiles { get; set; }
    public int CandidatesFound { get; set; }
    public int DuplicatesDetected { get; set; }
    public List<string> Warnings { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
    public DateTimeOffset StartedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? CompletedAtUtc { get; set; }
    public List<DetectedGameCandidate> Candidates { get; set; } = new();
}

public sealed class CoverSearchRequest
{
    public string GameTitle { get; set; } = string.Empty;
    public string Platform { get; set; } = "PC";
    public string ExternalAppId { get; set; } = string.Empty;
}

public sealed class CoverSearchResponse
{
    public string QueryTitle { get; set; } = string.Empty;
    public DetectedGameMetadata? Metadata { get; set; }
    public List<ArtworkCandidate> Candidates { get; set; } = new();
}

public sealed class ImportDetectedGameItem
{
    public string CandidateId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<string> CategoryIds { get; set; } = new();
    public NetworkMode NetworkMode { get; set; } = NetworkMode.Online;
    public string Description { get; set; } = string.Empty;
    public string Developer { get; set; } = string.Empty;
    public string StorageLocationId { get; set; } = "ccboot-primary";
    public string ExecutablePath { get; set; } = string.Empty;
    public string LaunchArguments { get; set; } = string.Empty;
    public LaunchType LaunchType { get; set; } = LaunchType.DirectExecutable;
    public string ExternalAppId { get; set; } = string.Empty;
    public string ProcessNameToMonitor { get; set; } = string.Empty;
    public string SelectedCoverUrl { get; set; } = string.Empty;
    public string HeroImagePath { get; set; } = string.Empty;
    public bool OverwriteExisting { get; set; }
}

public sealed class ImportDetectedGamesRequest
{
    public List<ImportDetectedGameItem> Items { get; set; } = new();
}

public sealed class ImportDetectedGamesResponse
{
    public int ImportedCount { get; set; }
    public int SkippedCount { get; set; }
    public List<GameEntry> Games { get; set; } = new();
    public List<string> SkippedReasons { get; set; } = new();
}
