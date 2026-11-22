using System.Collections.Concurrent;
using System.Diagnostics;
using CyberHub.ServerService.Data;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using CyberHub.Shared.Services;

namespace CyberHub.ServerService.Services;

public sealed class GameScannerService : IDisposable
{
    private sealed class ScanJobState
    {
        public GameScanJobSnapshot Snapshot { get; } = new();
        public CancellationTokenSource Cts { get; } = new();
    }

    private readonly SqliteGameRepository _repository;
    private readonly CoverArtLookupService _coverLookup;
    private readonly CatalogSyncService _catalogSync;
    private readonly ILogger<GameScannerService> _logger;
    private readonly ConcurrentDictionary<string, ScanJobState> _jobs = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _activeScanGate = new(1, 1);

    public GameScannerService(
        SqliteGameRepository repository,
        CoverArtLookupService coverLookup,
        CatalogSyncService catalogSync,
        ILogger<GameScannerService> logger)
    {
        _repository = repository;
        _coverLookup = coverLookup;
        _catalogSync = catalogSync;
        _logger = logger;
    }

    public async Task<GameScanJobSnapshot> StartBackgroundScanAsync(
        StartGameScanRequest request,
        string workstation,
        string sourceIp,
        CancellationToken callerCt = default)
    {
        List<StorageLocation> allLocations = await _repository.GetStorageLocationsAsync(callerCt);
        var targets = new List<(string LocationId, string RootPath)>();

        if (request.StorageLocationIds.Count > 0)
        {
            foreach (string id in request.StorageLocationIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                StorageLocation? match = allLocations.FirstOrDefault(l => string.Equals(l.Id, id, StringComparison.OrdinalIgnoreCase));
                if (match is not null)
                {
                    targets.Add((match.Id, match.UncRootPath));
                }
            }
        }
        else if (request.CustomPaths.Count == 0)
        {
            foreach (StorageLocation loc in allLocations)
            {
                targets.Add((loc.Id, loc.UncRootPath));
            }
        }

        foreach (string custom in request.CustomPaths)
        {
            (bool ok, _) = UncPathValidator.ValidateStorageRootPath(custom);
            if (ok)
            {
                targets.Add(("custom-folder", custom.Trim()));
            }
        }

        var job = new ScanJobState();
        job.Snapshot.Status = GameScanJobStatus.Running;
        job.Snapshot.StartedAtUtc = DateTimeOffset.UtcNow;
        _jobs[job.Snapshot.JobId] = job;

        await _repository.AppendAuditLogAsync(new AdminAuditLog
        {
            Action = "GAME_SCAN_STARTED",
            WorkstationName = workstation,
            SourceIp = sourceIp,
            Details = $"Started background scan job {job.Snapshot.JobId} across {targets.Count} location(s)."
        }, callerCt);

        int maxDepth = Math.Clamp(request.MaxDepth, 1, 6);
        _ = Task.Run(() => ExecuteScanAsync(job, targets, maxDepth, request.IncludeSteamManifests, request.IncludeRiotMetadata, request.SearchCoverArt));

        return CloneSnapshot(job.Snapshot);
    }

    public GameScanJobSnapshot? GetJobSnapshot(string jobId)
    {
        return _jobs.TryGetValue(jobId, out ScanJobState? job) ? CloneSnapshot(job.Snapshot) : null;
    }

    public bool CancelScan(string jobId)
    {
        if (_jobs.TryGetValue(jobId, out ScanJobState? job))
        {
            job.Cts.Cancel();
            return true;
        }
        return false;
    }

    public async Task<ImportDetectedGamesResponse> ImportConfirmedGamesAsync(
        ImportDetectedGamesRequest request,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        var response = new ImportDetectedGamesResponse();
        List<GameEntry> existingGames = await _repository.GetGamesAsync(includeHidden: true, ct);
        List<CategoryEntry> categories = await _repository.GetCategoriesAsync(ct);
        CategoryEntry? fallbackCategory = categories.FirstOrDefault(c => !c.IsSystem);

        foreach (ImportDetectedGameItem item in request.Items)
        {
            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.ExecutablePath))
            {
                response.SkippedCount++;
                response.SkippedReasons.Add($"Skipped '{item.Name}': missing name or executable path.");
                continue;
            }

            (bool validPath, string pathError) = UncPathValidator.ValidateGamePath(
                item.LaunchType,
                item.ExecutablePath,
                item.ExternalAppId,
                item.LaunchArguments);
            if (!validPath)
            {
                response.SkippedCount++;
                response.SkippedReasons.Add($"Skipped '{item.Name}': {pathError}");
                continue;
            }

            // Never overwrite admin-edited database records unless explicitly forced
            var probe = new DetectedGameCandidate
            {
                DetectedName = item.Name,
                NormalizedName = GameDetectionHeuristics.NormalizeGameName(item.Name),
                ExecutablePath = item.ExecutablePath,
                LaunchType = item.LaunchType,
                ExternalAppId = item.ExternalAppId
            };
            (bool isDup, string dupReason, Guid? existingId, _) = GameDetectionHeuristics.CheckDuplicate(probe, existingGames);

            if (isDup && !item.OverwriteExisting)
            {
                response.SkippedCount++;
                response.SkippedReasons.Add($"Skipped '{item.Name}': {dupReason}");
                continue;
            }

            string finalCoverPath = string.Empty;
            if (!string.IsNullOrWhiteSpace(item.SelectedCoverUrl))
            {
                if (item.SelectedCoverUrl.StartsWith("/covers/", StringComparison.OrdinalIgnoreCase))
                {
                    finalCoverPath = item.SelectedCoverUrl;
                }
                else if (UncPathValidator.IsSafeExternalArtworkUrl(item.SelectedCoverUrl))
                {
                    (bool downloaded, string localCover, string err) = await _coverLookup.DownloadAndValidateCoverAsync(item.SelectedCoverUrl, ct);
                    if (downloaded)
                    {
                        finalCoverPath = localCover;
                    }
                    else
                    {
                        response.SkippedReasons.Add($"Cover fallback notice for '{item.Name}': {err}");
                    }
                }
            }

            List<string> catIds = item.CategoryIds
                .Where(cid => categories.Any(c => !c.IsSystem && string.Equals(c.Id, cid, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (catIds.Count == 0 && fallbackCategory is not null)
            {
                catIds.Add(fallbackCategory.Id);
            }

            string primaryCategoryName = catIds.Count > 0
                ? categories.First(c => c.Id == catIds[0]).NameEn
                : "Action";

            var entry = new GameEntry
            {
                Id = (isDup && existingId.HasValue) ? existingId.Value : Guid.NewGuid(),
                Name = item.Name.Trim(),
                Category = primaryCategoryName,
                CategoryIds = catIds,
                NetworkMode = item.NetworkMode,
                Description = item.Description.Trim(),
                Developer = item.Developer.Trim(),
                CoverImagePath = finalCoverPath,
                HeroImagePath = UncPathValidator.IsSafeCoverImagePath(item.HeroImagePath) ? item.HeroImagePath.Trim() : string.Empty,
                StorageLocationId = string.IsNullOrWhiteSpace(item.StorageLocationId) ? "ccboot-primary" : item.StorageLocationId.Trim(),
                ExecutablePath = item.ExecutablePath.Trim(),
                LaunchArguments = item.LaunchArguments.Trim(),
                LaunchType = item.LaunchType,
                ExternalAppId = item.ExternalAppId.Trim(),
                ProcessNameToMonitor = string.IsNullOrWhiteSpace(item.ProcessNameToMonitor)
                    ? Path.GetFileNameWithoutExtension(item.ExecutablePath.Trim())
                    : item.ProcessNameToMonitor.Trim(),
                IsVisible = true,
                CreatedAtUtc = DateTimeOffset.UtcNow,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            };

            await _repository.UpsertGameAsync(entry, isNew: !(isDup && existingId.HasValue), ct);
            existingGames.Add(entry);
            response.Games.Add(entry);
            response.ImportedCount++;

            await _repository.AppendAuditLogAsync(new AdminAuditLog
            {
                Action = "GAME_SCAN_IMPORTED",
                TargetGameId = entry.Id.ToString(),
                TargetGameName = entry.Name,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Imported scanned game ({entry.ExecutablePath}) via confirmation workflow."
            }, ct);
        }

        if (response.ImportedCount > 0)
        {
            await _catalogSync.PublishSnapshotToShareAsync(ct);
        }

        return response;
    }

    private async Task ExecuteScanAsync(
        ScanJobState job,
        List<(string LocationId, string RootPath)> targets,
        int maxDepth,
        bool includeSteamManifests,
        bool includeRiotMetadata,
        bool searchCoverArt)
    {
        CancellationToken ct = job.Cts.Token;
        await _activeScanGate.WaitAsync(ct).ConfigureAwait(false);

        try
        {
            List<GameEntry> existingGames = await _repository.GetGamesAsync(includeHidden: true, ct);
            var seenExePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach ((string locId, string rootPath) in targets)
            {
                ct.ThrowIfCancellationRequested();

                if (!Directory.Exists(rootPath))
                {
                    lock (job.Snapshot)
                    {
                        job.Snapshot.Warnings.Add($"Storage path unavailable or offline: {rootPath}");
                    }
                    continue;
                }

                var steamManifestsByDir = new Dictionary<string, SteamManifestInfo>(StringComparer.OrdinalIgnoreCase);
                await ScanDirectoryRecursiveAsync(
                    job,
                    locId,
                    rootPath,
                    currentDepth: 0,
                    maxDepth,
                    includeSteamManifests,
                    includeRiotMetadata,
                    steamManifestsByDir,
                    seenExePaths,
                    existingGames,
                    ct);
            }

            if (searchCoverArt)
            {
                List<DetectedGameCandidate> candidatesSnapshot;
                lock (job.Snapshot)
                {
                    candidatesSnapshot = job.Snapshot.Candidates.ToList();
                }

                // Rate-limit external cover searches to avoid saturating LAN/API
                foreach (DetectedGameCandidate candidate in candidatesSnapshot.Take(25))
                {
                    ct.ThrowIfCancellationRequested();
                    CoverSearchResponse art = await _coverLookup.SearchArtworkAndMetadataAsync(
                        candidate.DetectedName,
                        candidate.ExternalAppId,
                        candidate.LaunchType == LaunchType.Steam ? "Steam" : "PC",
                        ct);

                    lock (job.Snapshot)
                    {
                        candidate.ArtworkCandidates = art.Candidates;
                        if (art.Metadata is not null)
                        {
                            candidate.Metadata = art.Metadata;
                        }
                        if (art.Candidates.Count > 0)
                        {
                            candidate.SuggestedCoverUrl = art.Candidates[0].ImageUrl;
                            candidate.SelectedCoverUrl = art.Candidates[0].ImageUrl;
                        }
                    }

                    await Task.Delay(120, ct);
                }
            }

            lock (job.Snapshot)
            {
                job.Snapshot.Status = GameScanJobStatus.Completed;
                job.Snapshot.CompletedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        catch (OperationCanceledException)
        {
            lock (job.Snapshot)
            {
                job.Snapshot.Status = GameScanJobStatus.Cancelled;
                job.Snapshot.CompletedAtUtc = DateTimeOffset.UtcNow;
                job.Snapshot.Warnings.Add("Scan cancelled cleanly by administrator.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Game scan job {JobId} failed.", job.Snapshot.JobId);
            lock (job.Snapshot)
            {
                job.Snapshot.Status = GameScanJobStatus.Failed;
                job.Snapshot.ErrorMessage = ex.Message;
                job.Snapshot.CompletedAtUtc = DateTimeOffset.UtcNow;
            }
        }
        finally
        {
            _activeScanGate.Release();
        }
    }

    private async Task ScanDirectoryRecursiveAsync(
        ScanJobState job,
        string locId,
        string dirPath,
        int currentDepth,
        int maxDepth,
        bool includeSteamManifests,
        bool includeRiotMetadata,
        Dictionary<string, SteamManifestInfo> steamManifestsByDir,
        HashSet<string> seenExePaths,
        List<GameEntry> existingGames,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        lock (job.Snapshot)
        {
            job.Snapshot.ScannedDirectories++;
        }

        string[] files;
        try
        {
            files = Directory.GetFiles(dirPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            lock (job.Snapshot)
            {
                job.Snapshot.Warnings.Add($"Restricted directory skipped: {dirPath}");
            }
            return;
        }

        if (includeSteamManifests)
        {
            foreach (string file in files)
            {
                string fname = Path.GetFileName(file);
                if (fname.StartsWith("appmanifest_", StringComparison.OrdinalIgnoreCase) &&
                    fname.EndsWith(".acf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        string content = await File.ReadAllTextAsync(file, ct);
                        SteamManifestInfo? info = GameDetectionHeuristics.ParseSteamAppManifest(content);
                        if (info is not null && !string.IsNullOrWhiteSpace(info.InstallDir))
                        {
                            string installFolder = Path.Combine(dirPath, "common", info.InstallDir);
                            steamManifestsByDir[installFolder] = info;
                            steamManifestsByDir[Path.Combine(dirPath, info.InstallDir)] = info;
                        }
                    }
                    catch (Exception)
                    {
                        // Ignore unreadable manifest
                    }
                }
            }
        }

        foreach (string file in files)
        {
            ct.ThrowIfCancellationRequested();
            lock (job.Snapshot)
            {
                job.Snapshot.ScannedFiles++;
            }

            if (GameDetectionHeuristics.ShouldIgnorePathOrFile(file))
            {
                continue;
            }

            if (!seenExePaths.Add(file))
            {
                continue;
            }

            DetectedGameCandidate candidate = BuildCandidateFromExecutable(file, locId, includeRiotMetadata, steamManifestsByDir);
            (bool isDup, string dupReason, Guid? existingId, string existingName) = GameDetectionHeuristics.CheckDuplicate(candidate, existingGames);

            candidate.IsDuplicate = isDup;
            candidate.DuplicateReason = dupReason;
            candidate.DuplicateExistingGameId = existingId;
            candidate.DuplicateExistingGameName = existingName;
            candidate.IsSelected = !isDup;

            lock (job.Snapshot)
            {
                job.Snapshot.Candidates.Add(candidate);
                job.Snapshot.CandidatesFound = job.Snapshot.Candidates.Count;
                if (isDup)
                {
                    job.Snapshot.DuplicatesDetected++;
                }
            }
        }

        if (currentDepth >= maxDepth)
        {
            return;
        }

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dirPath);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return;
        }

        foreach (string subDir in subDirs)
        {
            ct.ThrowIfCancellationRequested();
            // Throttle SMB traversal gently so active CCBoot clients are unaffected
            if (job.Snapshot.ScannedDirectories % 20 == 0)
            {
                await Task.Delay(5, ct);
            }

            await ScanDirectoryRecursiveAsync(
                job,
                locId,
                subDir,
                currentDepth + 1,
                maxDepth,
                includeSteamManifests,
                includeRiotMetadata,
                steamManifestsByDir,
                seenExePaths,
                existingGames,
                ct);
        }
    }

    private static DetectedGameCandidate BuildCandidateFromExecutable(
        string exePath,
        string storageLocationId,
        bool includeRiotMetadata,
        Dictionary<string, SteamManifestInfo> steamManifestsByDir)
    {
        string? productName = null;
        string? fileDesc = null;
        string? companyName = null;

        try
        {
            // Read-only PE resource inspection; never executes the binary
            FileVersionInfo version = FileVersionInfo.GetVersionInfo(exePath);
            productName = version.ProductName;
            fileDesc = version.FileDescription;
            companyName = version.CompanyName;
        }
        catch (Exception)
        {
            // Non-Windows or missing PE resources handled cleanly via filename heuristics
        }

        LaunchType launchType = LaunchType.DirectExecutable;
        string externalAppId = string.Empty;
        string detectedName = GameDetectionHeuristics.DeriveReadableNameFromFileOrFolder(exePath, productName, fileDesc);
        ScanConfidence confidence = !string.IsNullOrWhiteSpace(productName) ? ScanConfidence.Medium : ScanConfidence.Low;
        int score = !string.IsNullOrWhiteSpace(productName) ? 72 : 52;
        string source = !string.IsNullOrWhiteSpace(productName) ? "Executable PE Metadata" : "Directory & Filename Heuristics";

        foreach ((string manifestDir, SteamManifestInfo info) in steamManifestsByDir)
        {
            if (exePath.StartsWith(manifestDir, StringComparison.OrdinalIgnoreCase))
            {
                launchType = LaunchType.Steam;
                externalAppId = info.AppId;
                detectedName = info.Name;
                confidence = ScanConfidence.High;
                score = 96;
                source = $"Steam Manifest (AppID {info.AppId})";
                break;
            }
        }

        if (launchType == LaunchType.DirectExecutable && includeRiotMetadata)
        {
            (bool isRiot, string productId, string riotName) = GameDetectionHeuristics.DetectRiotByFolderOrBinary(exePath);
            if (isRiot)
            {
                launchType = LaunchType.RiotClient;
                externalAppId = productId;
                detectedName = riotName;
                confidence = ScanConfidence.High;
                score = 94;
                source = $"Riot Product Metadata ({productId})";
            }
        }

        return new DetectedGameCandidate
        {
            DetectedName = detectedName,
            NormalizedName = GameDetectionHeuristics.NormalizeGameName(detectedName),
            ExecutablePath = exePath,
            LaunchType = launchType,
            ExternalAppId = externalAppId,
            ProcessNameToMonitor = Path.GetFileNameWithoutExtension(exePath),
            StorageLocationId = storageLocationId,
            SourceFolder = Path.GetDirectoryName(exePath) ?? string.Empty,
            Confidence = confidence,
            ConfidenceScore = score,
            DetectionSource = source,
            Metadata = new DetectedGameMetadata
            {
                OfficialTitle = detectedName,
                Developer = companyName?.Trim() ?? string.Empty,
                Platform = launchType == LaunchType.Steam ? "PC (Steam)" : "PC"
            }
        };
    }

    private static GameScanJobSnapshot CloneSnapshot(GameScanJobSnapshot src)
    {
        lock (src)
        {
            return new GameScanJobSnapshot
            {
                JobId = src.JobId,
                Status = src.Status,
                ScannedDirectories = src.ScannedDirectories,
                ScannedFiles = src.ScannedFiles,
                CandidatesFound = src.CandidatesFound,
                DuplicatesDetected = src.DuplicatesDetected,
                Warnings = new List<string>(src.Warnings),
                ErrorMessage = src.ErrorMessage,
                StartedAtUtc = src.StartedAtUtc,
                CompletedAtUtc = src.CompletedAtUtc,
                Candidates = src.Candidates.ToList()
            };
        }
    }

    public void Dispose()
    {
        foreach (ScanJobState state in _jobs.Values)
        {
            state.Cts.Dispose();
        }
        _activeScanGate.Dispose();
    }
}
