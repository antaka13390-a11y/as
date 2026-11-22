using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CyberHub.AdminPanel.Localization;
using CyberHub.AdminPanel.Services;
using CyberHub.Shared.Models;

namespace CyberHub.AdminPanel.ViewModels;

public sealed class SelectableStorageLocationItem : INotifyPropertyChanged
{
    private bool _isChecked = true;
    public required StorageLocation Location { get; init; }
    public bool IsChecked
    {
        get => _isChecked;
        set { _isChecked = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked))); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DetectedCandidateItem : INotifyPropertyChanged
{
    private bool _isSelected;
    private bool _isIgnored;
    private string _editableName;
    private string _selectedCoverUrl;
    private NetworkMode _networkMode = NetworkMode.Online;
    private string _description;
    private string _developer;

    public DetectedGameCandidate Source { get; }
    public ObservableCollection<ArtworkCandidate> ArtworkOptions { get; }
    public ObservableCollection<SelectableCategoryItem> CandidateCategories { get; } = new();

    public DetectedCandidateItem(DetectedGameCandidate source, IEnumerable<CategoryEntry> customCategories, AdminLocalizationService loc)
    {
        Source = source;
        _isSelected = source.IsSelected && !source.IsDuplicate;
        _isIgnored = source.IsIgnored;
        _editableName = source.DetectedName;
        _selectedCoverUrl = source.SelectedCoverUrl;
        _description = source.Metadata.ShortDescription;
        _developer = source.Metadata.Developer;
        ArtworkOptions = new ObservableCollection<ArtworkCandidate>(source.ArtworkCandidates);

        foreach (CategoryEntry cat in customCategories)
        {
            CandidateCategories.Add(new SelectableCategoryItem
            {
                Category = cat,
                Loc = loc,
                IsSelected = string.Equals(cat.NameEn, source.Metadata.Genre, StringComparison.OrdinalIgnoreCase)
            });
        }
    }

    public bool IsSelected { get => _isSelected; set { _isSelected = value; OnPropertyChanged(); } }
    public bool IsIgnored { get => _isIgnored; set { _isIgnored = value; OnPropertyChanged(); } }
    public string EditableName { get => _editableName; set { _editableName = value; OnPropertyChanged(); } }
    public string SelectedCoverUrl { get => _selectedCoverUrl; set { _selectedCoverUrl = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCover)); } }
    public bool HasCover => !string.IsNullOrWhiteSpace(SelectedCoverUrl);
    public NetworkMode NetworkMode { get => _networkMode; set { _networkMode = value; OnPropertyChanged(); } }
    public string Description { get => _description; set { _description = value; OnPropertyChanged(); } }
    public string Developer { get => _developer; set { _developer = value; OnPropertyChanged(); } }

    public string ConfidenceBadge => $"{Source.Confidence} ({Source.ConfidenceScore}%) · {Source.DetectionSource}";
    public string MetadataLine => string.Join(" · ", new[]
    {
        Source.Metadata.ReleaseYear,
        Source.Metadata.Genre,
        Source.Metadata.Platform
    }.Where(s => !string.IsNullOrWhiteSpace(s)));

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class GameScannerViewModel : INotifyPropertyChanged
{
    private readonly AdminApiClient _apiClient;
    private readonly AdminLocalizationService _loc;
    private readonly Action<string> _setStatus;
    private readonly Func<Task> _refreshMainLibraryAsync;

    private DetectedCandidateItem? _selectedCandidate;
    private string _activeJobId = string.Empty;
    private bool _isScanning;
    private string _scanProgressSummary = "Ready to scan configured CCBoot & SMB game folders.";
    private string _newLocationName = string.Empty;
    private string _newLocationPath = @"\\CCBOOT-SRV\Games\";
    private string _newLocationDescription = string.Empty;
    private string _customFolderScanPath = string.Empty;
    private string _coverSearchQuery = string.Empty;
    private bool _includeSteamManifests = true;
    private bool _includeRiotMetadata = true;
    private bool _searchCoverArt = true;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<SelectableStorageLocationItem> StorageTargets { get; } = new();
    public ObservableCollection<DetectedCandidateItem> Candidates { get; } = new();
    public ObservableCollection<string> ScanWarnings { get; } = new();
    public Array NetworkModes => Enum.GetValues(typeof(NetworkMode));

    public GameScannerViewModel(
        AdminApiClient apiClient,
        AdminLocalizationService loc,
        Action<string> setStatus,
        Func<Task> refreshMainLibraryAsync)
    {
        _apiClient = apiClient;
        _loc = loc;
        _setStatus = setStatus;
        _refreshMainLibraryAsync = refreshMainLibraryAsync;
    }

    public DetectedCandidateItem? SelectedCandidate
    {
        get => _selectedCandidate;
        set
        {
            _selectedCandidate = value;
            OnPropertyChanged();
            if (value is not null)
            {
                CoverSearchQuery = value.EditableName;
            }
        }
    }

    public bool IsScanning { get => _isScanning; set { _isScanning = value; OnPropertyChanged(); } }
    public string ScanProgressSummary { get => _scanProgressSummary; set { _scanProgressSummary = value; OnPropertyChanged(); } }
    public string NewLocationName { get => _newLocationName; set { _newLocationName = value; OnPropertyChanged(); } }
    public string NewLocationPath { get => _newLocationPath; set { _newLocationPath = value; OnPropertyChanged(); } }
    public string NewLocationDescription { get => _newLocationDescription; set { _newLocationDescription = value; OnPropertyChanged(); } }
    public string CustomFolderScanPath { get => _customFolderScanPath; set { _customFolderScanPath = value; OnPropertyChanged(); } }
    public string CoverSearchQuery { get => _coverSearchQuery; set { _coverSearchQuery = value; OnPropertyChanged(); } }
    public bool IncludeSteamManifests { get => _includeSteamManifests; set { _includeSteamManifests = value; OnPropertyChanged(); } }
    public bool IncludeRiotMetadata { get => _includeRiotMetadata; set { _includeRiotMetadata = value; OnPropertyChanged(); } }
    public bool SearchCoverArt { get => _searchCoverArt; set { _searchCoverArt = value; OnPropertyChanged(); } }

    public void SyncStorageLocations(IEnumerable<StorageLocation> locations)
    {
        StorageTargets.Clear();
        foreach (StorageLocation loc in locations)
        {
            StorageTargets.Add(new SelectableStorageLocationItem { Location = loc, IsChecked = true });
        }
    }

    public async Task AddStorageLocationAsync()
    {
        try
        {
            StorageLocation saved = await _apiClient.SaveStorageLocationAsync(new UpsertStorageLocationRequest
            {
                DisplayName = NewLocationName,
                UncRootPath = NewLocationPath,
                Description = NewLocationDescription,
                IsReadOnlyForClients = true
            });

            StorageTargets.Add(new SelectableStorageLocationItem { Location = saved, IsChecked = true });
            NewLocationName = string.Empty;
            NewLocationDescription = string.Empty;
            _setStatus($"Added storage folder '{saved.DisplayName}' ({saved.UncRootPath}).");
        }
        catch (Exception ex)
        {
            _setStatus($"Add storage folder failed: {ex.Message}");
        }
    }

    public async Task DeleteStorageLocationAsync(SelectableStorageLocationItem item)
    {
        try
        {
            await _apiClient.DeleteStorageLocationAsync(item.Location.Id);
            StorageTargets.Remove(item);
            _setStatus($"Removed storage location '{item.Location.DisplayName}' (files untouched).");
        }
        catch (Exception ex)
        {
            _setStatus($"Remove storage location failed: {ex.Message}");
        }
    }

    public async Task StartBackgroundScanAsync(IReadOnlyList<CategoryEntry> categories)
    {
        if (IsScanning) return;

        IsScanning = true;
        Candidates.Clear();
        ScanWarnings.Clear();
        ScanProgressSummary = "Starting background scan...";

        try
        {
            var req = new StartGameScanRequest
            {
                StorageLocationIds = StorageTargets.Where(t => t.IsChecked).Select(t => t.Location.Id).ToList(),
                CustomPaths = string.IsNullOrWhiteSpace(CustomFolderScanPath) ? new() : new() { CustomFolderScanPath.Trim() },
                MaxDepth = 4,
                IncludeSteamManifests = IncludeSteamManifests,
                IncludeRiotMetadata = IncludeRiotMetadata,
                SearchCoverArt = SearchCoverArt
            };

            GameScanJobSnapshot job = await _apiClient.StartGameScanAsync(req);
            _activeJobId = job.JobId;

            while (job.Status == GameScanJobStatus.Running)
            {
                await Task.Delay(500);
                job = await _apiClient.GetScanStatusAsync(_activeJobId);
                ScanProgressSummary = $"Status: {job.Status} | Directories: {job.ScannedDirectories} | Files: {job.ScannedFiles} | Detected: {job.CandidatesFound} | Duplicates: {job.DuplicatesDetected}";
            }

            List<CategoryEntry> customCats = categories.Where(c => !c.IsSystem).ToList();
            foreach (DetectedGameCandidate candidate in job.Candidates)
            {
                Candidates.Add(new DetectedCandidateItem(candidate, customCats, _loc));
            }
            foreach (string warning in job.Warnings)
            {
                ScanWarnings.Add(warning);
            }

            SelectedCandidate = Candidates.FirstOrDefault();
            ScanProgressSummary = $"Completed ({job.Status}): {job.CandidatesFound} detected, {job.DuplicatesDetected} duplicates across {job.ScannedDirectories} directories.";
            _setStatus(ScanProgressSummary);
        }
        catch (Exception ex)
        {
            ScanProgressSummary = $"Scan failed: {ex.Message}";
            _setStatus(ScanProgressSummary);
        }
        finally
        {
            IsScanning = false;
        }
    }

    public async Task CancelActiveScanAsync()
    {
        if (!string.IsNullOrWhiteSpace(_activeJobId))
        {
            try
            {
                await _apiClient.CancelGameScanAsync(_activeJobId);
                _setStatus("Cancellation requested for active scan job.");
            }
            catch (Exception ex)
            {
                _setStatus($"Cancel failed: {ex.Message}");
            }
        }
    }

    public async Task SearchCoverArtForSelectedAsync()
    {
        if (SelectedCandidate is null) return;
        try
        {
            CoverSearchResponse resp = await _apiClient.SearchCoverArtAsync(new CoverSearchRequest
            {
                GameTitle = string.IsNullOrWhiteSpace(CoverSearchQuery) ? SelectedCandidate.EditableName : CoverSearchQuery,
                ExternalAppId = SelectedCandidate.Source.ExternalAppId,
                Platform = SelectedCandidate.Source.LaunchType == LaunchType.Steam ? "Steam" : "PC"
            });

            SelectedCandidate.ArtworkOptions.Clear();
            foreach (ArtworkCandidate art in resp.Candidates)
            {
                SelectedCandidate.ArtworkOptions.Add(art);
            }

            if (resp.Candidates.Count > 0)
            {
                SelectedCandidate.SelectedCoverUrl = resp.Candidates[0].ImageUrl;
            }
            if (resp.Metadata is not null && string.IsNullOrWhiteSpace(SelectedCandidate.Description))
            {
                SelectedCandidate.Description = resp.Metadata.ShortDescription;
                SelectedCandidate.Developer = string.IsNullOrWhiteSpace(SelectedCandidate.Developer)
                    ? resp.Metadata.Developer
                    : SelectedCandidate.Developer;
            }

            OnPropertyChanged(nameof(SelectedCandidate));
            _setStatus($"Found {resp.Candidates.Count} cover artwork candidate(s) for '{SelectedCandidate.EditableName}'.");
        }
        catch (Exception ex)
        {
            _setStatus($"Artwork search failed: {ex.Message}");
        }
    }

    public async Task UploadManualCoverForSelectedAsync(string localFilePath)
    {
        if (SelectedCandidate is null) return;
        try
        {
            string relativeUrl = await _apiClient.UploadScannerArtworkAsync(localFilePath);
            SelectedCandidate.SelectedCoverUrl = _apiClient.ResolveServerAssetUrl(relativeUrl);
            SelectedCandidate.Source.SelectedCoverUrl = relativeUrl;
            _setStatus($"Uploaded validated cover artwork '{relativeUrl}'.");
        }
        catch (Exception ex)
        {
            _setStatus($"Manual cover upload failed: {ex.Message}");
        }
    }

    public void RemoveCoverFromSelected()
    {
        if (SelectedCandidate is null) return;
        SelectedCandidate.SelectedCoverUrl = string.Empty;
        SelectedCandidate.Source.SelectedCoverUrl = string.Empty;
        _setStatus($"Removed cover artwork from '{SelectedCandidate.EditableName}'.");
    }

    public async Task ImportSelectedCandidatesAsync()
    {
        var selected = Candidates.Where(c => c.IsSelected && !c.IsIgnored).ToList();
        if (selected.Count == 0)
        {
            _setStatus("Select at least one non-ignored candidate game to import.");
            return;
        }

        try
        {
            var req = new ImportDetectedGamesRequest
            {
                Items = selected.Select(c => new ImportDetectedGameItem
                {
                    CandidateId = c.Source.CandidateId,
                    Name = c.EditableName,
                    CategoryIds = c.CandidateCategories.Where(cat => cat.IsSelected).Select(cat => cat.Category.Id).ToList(),
                    NetworkMode = c.NetworkMode,
                    Description = c.Description,
                    Developer = c.Developer,
                    StorageLocationId = c.Source.StorageLocationId,
                    ExecutablePath = c.Source.ExecutablePath,
                    LaunchArguments = c.Source.LaunchArguments,
                    LaunchType = c.Source.LaunchType,
                    ExternalAppId = c.Source.ExternalAppId,
                    ProcessNameToMonitor = c.Source.ProcessNameToMonitor,
                    SelectedCoverUrl = !string.IsNullOrWhiteSpace(c.Source.SelectedCoverUrl)
                        ? c.Source.SelectedCoverUrl
                        : c.SelectedCoverUrl,
                    HeroImagePath = c.Source.Metadata.HeroImageUrl,
                    OverwriteExisting = false
                }).ToList()
            };

            ImportDetectedGamesResponse resp = await _apiClient.ImportDetectedGamesAsync(req);
            await _refreshMainLibraryAsync();
            _setStatus($"Imported {resp.ImportedCount} game(s), skipped {resp.SkippedCount}.");
        }
        catch (Exception ex)
        {
            _setStatus($"Import failed: {ex.Message}");
        }
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
