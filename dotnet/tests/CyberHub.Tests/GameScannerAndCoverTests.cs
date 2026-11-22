using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.ServerService.Services;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using CyberHub.Shared.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CyberHub.Tests;

public sealed class GameScannerAndCoverTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServerOptions _options;
    private readonly SqliteGameRepository _repository;

    public GameScannerAndCoverTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"cyberhub-scan-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _options = new ServerOptions
        {
            DatabasePath = Path.Combine(_tempRoot, "cyberhub.db"),
            BackupDirectory = Path.Combine(_tempRoot, "backups"),
            PublishedCatalogPath = Path.Combine(_tempRoot, "published", "catalog.json"),
            CoversDirectory = Path.Combine(_tempRoot, "published", "covers"),
            PublishedThemePath = Path.Combine(_tempRoot, "published", "theme.json"),
            ThemeAssetsDirectory = Path.Combine(_tempRoot, "published", "theme-assets")
        };

        _repository = new SqliteGameRepository(Options.Create(_options));
    }

    [Fact]
    public void FalsePositiveFiltering_IgnoresSystemFoldersInstallersAndUtilities()
    {
        Assert.True(GameDetectionHeuristics.ShouldIgnorePathOrFile(@"C:\Windows\System32\cmd.exe"));
        Assert.True(GameDetectionHeuristics.ShouldIgnorePathOrFile(@"\\CCBOOT-SRV\Games\Temp\vcredist_x64.exe"));
        Assert.True(GameDetectionHeuristics.ShouldIgnorePathOrFile(@"\\CCBOOT-SRV\Games\Valorant\unins000.exe"));
        Assert.True(GameDetectionHeuristics.ShouldIgnorePathOrFile(@"\\CCBOOT-SRV\Games\Demo\CrashReportClient.exe"));

        Assert.False(GameDetectionHeuristics.ShouldIgnorePathOrFile(@"\\CCBOOT-SRV\Games\EldenRing\eldenring.exe"));
    }

    [Fact]
    public void SteamManifestAndDuplicateDetection_IdentifiesDuplicatesWithoutModifyingRecords()
    {
        const string acf = @"
""AppState""
{
    ""appid"" ""730""
    ""name"" ""Counter-Strike 2""
    ""installdir"" ""Counter-Strike Global Offensive""
}";
        SteamManifestInfo? parsed = GameDetectionHeuristics.ParseSteamAppManifest(acf);
        Assert.NotNull(parsed);
        Assert.Equal("730", parsed!.AppId);
        Assert.Equal("Counter-Strike 2", parsed.Name);

        var existingGames = new List<GameEntry>
        {
            new()
            {
                Id = Guid.NewGuid(),
                Name = "Counter-Strike 2",
                LaunchType = LaunchType.Steam,
                ExternalAppId = "730",
                ExecutablePath = @"\\CCBOOT-SRV\Games\SteamLibrary\steamapps\common\Counter-Strike Global Offensive\cs2.exe"
            }
        };

        var dupCandidate = new DetectedGameCandidate
        {
            DetectedName = "Counter-Strike 2™",
            NormalizedName = GameDetectionHeuristics.NormalizeGameName("Counter-Strike 2™"),
            LaunchType = LaunchType.Steam,
            ExternalAppId = "730",
            ExecutablePath = @"D:\AltSteam\cs2.exe"
        };

        (bool isDup, string reason, Guid? existingId, _) = GameDetectionHeuristics.CheckDuplicate(dupCandidate, existingGames);
        Assert.True(isDup);
        Assert.Equal(existingGames[0].Id, existingId);
        Assert.Contains("730", reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackgroundScanner_DetectsGames_HandlesUnavailablePaths_SupportsCancel_AndPreservesExistingAdminGames()
    {
        await _repository.InitializeAsync();

        // Pre-existing admin-edited game record that MUST NOT be overwritten
        var existingGame = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "My Custom Edited Title",
            ExecutablePath = Path.Combine(_tempRoot, "vault", "ExistingGame", "ExistingGame.exe"),
            Description = "Admin custom café instructions",
            IsVisible = true
        };
        await _repository.UpsertGameAsync(existingGame, isNew: true);

        string vaultDir = Path.Combine(_tempRoot, "vault");
        Directory.CreateDirectory(Path.Combine(vaultDir, "ExistingGame"));
        Directory.CreateDirectory(Path.Combine(vaultDir, "ApexOverdrive"));
        Directory.CreateDirectory(Path.Combine(vaultDir, "ApexOverdrive", "_CommonRedist"));

        await File.WriteAllBytesAsync(Path.Combine(vaultDir, "ExistingGame", "ExistingGame.exe"), new byte[] { 0x4D, 0x5A });
        await File.WriteAllBytesAsync(Path.Combine(vaultDir, "ApexOverdrive", "ApexOverdrive.exe"), new byte[] { 0x4D, 0x5A });
        await File.WriteAllBytesAsync(Path.Combine(vaultDir, "ApexOverdrive", "unins000.exe"), new byte[] { 0x4D, 0x5A });
        await File.WriteAllBytesAsync(Path.Combine(vaultDir, "ApexOverdrive", "_CommonRedist", "dxsetup.exe"), new byte[] { 0x4D, 0x5A });

        var coverLookup = new CoverArtLookupService(new HttpClient(), Options.Create(_options), NullLogger<CoverArtLookupService>.Instance);
        var sync = new CatalogSyncService(_repository, Options.Create(_options), NullLogger<CatalogSyncService>.Instance);
        using var scanner = new GameScannerService(_repository, coverLookup, sync, NullLogger<GameScannerService>.Instance);

        string offlinePath = Path.Combine(_tempRoot, "nonexistent-share");
        GameScanJobSnapshot job = await scanner.StartBackgroundScanAsync(new StartGameScanRequest
        {
            CustomPaths = new() { vaultDir, offlinePath },
            SearchCoverArt = false
        }, "ADMIN-PC", "192.168.10.50");

        GameScanJobSnapshot? current = scanner.GetJobSnapshot(job.JobId);
        int safetyTicks = 0;
        while (current is not null && current.Status == GameScanJobStatus.Running && safetyTicks++ < 60)
        {
            await Task.Delay(50);
            current = scanner.GetJobSnapshot(job.JobId);
        }

        Assert.NotNull(current);
        Assert.Equal(GameScanJobStatus.Completed, current!.Status);
        Assert.Contains(current.Warnings, w => w.Contains("nonexistent-share", StringComparison.OrdinalIgnoreCase));

        // Installers and redistributables must be filtered out
        Assert.DoesNotContain(current.Candidates, c => c.ExecutablePath.Contains("unins000", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(current.Candidates, c => c.ExecutablePath.Contains("dxsetup", StringComparison.OrdinalIgnoreCase));

        DetectedGameCandidate? dupItem = current.Candidates.FirstOrDefault(c => c.ExecutablePath.Contains("ExistingGame.exe", StringComparison.OrdinalIgnoreCase));
        Assert.NotNull(dupItem);
        Assert.True(dupItem!.IsDuplicate);

        // Importing both items with OverwriteExisting=false skips ExistingGame and imports ApexOverdrive cleanly
        ImportDetectedGamesResponse importResult = await scanner.ImportConfirmedGamesAsync(new ImportDetectedGamesRequest
        {
            Items = current.Candidates.Select(c => new ImportDetectedGameItem
            {
                CandidateId = c.CandidateId,
                Name = c.DetectedName,
                ExecutablePath = c.ExecutablePath,
                LaunchType = c.LaunchType,
                OverwriteExisting = false
            }).ToList()
        }, "ADMIN-PC", "192.168.10.50");

        Assert.Equal(1, importResult.ImportedCount);
        Assert.Equal(1, importResult.SkippedCount);

        GameEntry? preserved = await _repository.GetGameByIdAsync(existingGame.Id);
        Assert.NotNull(preserved);
        Assert.Equal("My Custom Edited Title", preserved!.Name);
        Assert.Equal("Admin custom café instructions", preserved.Description);
    }

    [Fact]
    public void ArtworkUrlAllowlist_BlocksSsrfLocalAndArbitraryHosts()
    {
        Assert.True(UncPathValidator.IsSafeExternalArtworkUrl("https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/730/library_600x900_2x.jpg"));
        Assert.False(UncPathValidator.IsSafeExternalArtworkUrl("http://127.0.0.1:8080/internal.png"));
        Assert.False(UncPathValidator.IsSafeExternalArtworkUrl("https://169.254.169.254/latest/meta-data"));
        Assert.False(UncPathValidator.IsSafeExternalArtworkUrl("file:///C:/Windows/win.ini"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
