using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CyberHub.AdminPanel.Services;
using CyberHub.Shared.Models;

namespace CyberHub.AdminPanel.ViewModels;

public sealed class AdminViewModel : INotifyPropertyChanged
{
    private readonly AdminApiClient _apiClient;
    private GameEntry? _selectedGame;
    private string _statusBanner = "Ready";
    private bool _isBusy;

    // Editor fields
    private string _editName = string.Empty;
    private string _editCategory = "Tactical FPS";
    private string _editDeveloper = string.Empty;
    private string _editDescription = string.Empty;
    private string _editCoverImagePath = string.Empty;
    private string _editStorageLocationId = "ccboot-primary";
    private string _editExecutablePath = string.Empty;
    private string _editLaunchArguments = string.Empty;
    private LaunchType _editLaunchType = LaunchType.DirectExecutable;
    private string _editExternalAppId = string.Empty;
    private string _editProcessNameToMonitor = string.Empty;
    private bool _editIsVisible = true;
    private bool _editIsFeatured;

    public event PropertyChangedEventHandler? PropertyChanged;

    public ObservableCollection<GameEntry> Games { get; } = new();
    public ObservableCollection<StorageLocation> StorageLocations { get; } = new();
    public ObservableCollection<AdminAuditLog> AuditLogs { get; } = new();

    public Array LaunchTypes => Enum.GetValues(typeof(LaunchType));
    public string[] PresetCategories { get; } =
    {
        "Tactical FPS", "MOBA", "Battle Royale", "Action RPG", "Racing", "Sports", "Co-Op", "Strategy"
    };

    public AdminViewModel(AdminApiClient apiClient)
    {
        _apiClient = apiClient;
    }

    public GameEntry? SelectedGame
    {
        get => _selectedGame;
        set
        {
            _selectedGame = value;
            OnPropertyChanged();
            LoadEditorFromSelection();
        }
    }

    public string StatusBanner
    {
        get => _statusBanner;
        set { _statusBanner = value; OnPropertyChanged(); }
    }

    public bool IsBusy
    {
        get => _isBusy;
        set { _isBusy = value; OnPropertyChanged(); }
    }

    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }
    public string EditCategory { get => _editCategory; set { _editCategory = value; OnPropertyChanged(); } }
    public string EditDeveloper { get => _editDeveloper; set { _editDeveloper = value; OnPropertyChanged(); } }
    public string EditDescription { get => _editDescription; set { _editDescription = value; OnPropertyChanged(); } }
    public string EditCoverImagePath { get => _editCoverImagePath; set { _editCoverImagePath = value; OnPropertyChanged(); } }
    public string EditStorageLocationId { get => _editStorageLocationId; set { _editStorageLocationId = value; OnPropertyChanged(); } }
    public string EditExecutablePath { get => _editExecutablePath; set { _editExecutablePath = value; OnPropertyChanged(); } }
    public string EditLaunchArguments { get => _editLaunchArguments; set { _editLaunchArguments = value; OnPropertyChanged(); } }
    public LaunchType EditLaunchType { get => _editLaunchType; set { _editLaunchType = value; OnPropertyChanged(); } }
    public string EditExternalAppId { get => _editExternalAppId; set { _editExternalAppId = value; OnPropertyChanged(); } }
    public string EditProcessNameToMonitor { get => _editProcessNameToMonitor; set { _editProcessNameToMonitor = value; OnPropertyChanged(); } }
    public bool EditIsVisible { get => _editIsVisible; set { _editIsVisible = value; OnPropertyChanged(); } }
    public bool EditIsFeatured { get => _editIsFeatured; set { _editIsFeatured = value; OnPropertyChanged(); } }

    public async Task RefreshAllAsync()
    {
        IsBusy = true;
        try
        {
            List<StorageLocation> locations = await _apiClient.GetStorageLocationsAsync();
            StorageLocations.Clear();
            foreach (StorageLocation loc in locations) StorageLocations.Add(loc);

            List<GameEntry> games = await _apiClient.GetGamesAsync();
            Games.Clear();
            foreach (GameEntry g in games) Games.Add(g);

            List<AdminAuditLog> logs = await _apiClient.GetLogsAsync();
            AuditLogs.Clear();
            foreach (AdminAuditLog log in logs) AuditLogs.Add(log);

            StatusBanner = $"Synchronized {Games.Count} library records and {AuditLogs.Count} audit entries.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Error: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public void BeginNewGame()
    {
        SelectedGame = null;
        EditName = string.Empty;
        EditCategory = "Tactical FPS";
        EditDeveloper = string.Empty;
        EditDescription = string.Empty;
        EditCoverImagePath = string.Empty;
        EditStorageLocationId = StorageLocations.FirstOrDefault()?.Id ?? "ccboot-primary";
        EditExecutablePath = @"\\CCBOOT-SRV\Games\CCBoot\";
        EditLaunchArguments = string.Empty;
        EditLaunchType = LaunchType.DirectExecutable;
        EditExternalAppId = string.Empty;
        EditProcessNameToMonitor = string.Empty;
        EditIsVisible = true;
        EditIsFeatured = false;
        StatusBanner = "Creating new game record.";
    }

    public async Task SaveGameAsync()
    {
        IsBusy = true;
        try
        {
            var req = new UpsertGameRequest
            {
                Name = EditName,
                Category = EditCategory,
                Developer = EditDeveloper,
                Description = EditDescription,
                CoverImagePath = EditCoverImagePath,
                StorageLocationId = EditStorageLocationId,
                ExecutablePath = EditExecutablePath,
                LaunchArguments = EditLaunchArguments,
                LaunchType = EditLaunchType,
                ExternalAppId = EditExternalAppId,
                ProcessNameToMonitor = EditProcessNameToMonitor,
                IsVisible = EditIsVisible,
                IsFeatured = EditIsFeatured
            };

            if (SelectedGame is null)
            {
                await _apiClient.CreateGameAsync(req);
                StatusBanner = $"Added '{EditName}' to central library.";
            }
            else
            {
                await _apiClient.UpdateGameAsync(SelectedGame.Id, req);
                StatusBanner = $"Updated '{EditName}'.";
            }

            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task ToggleSelectedVisibilityAsync()
    {
        if (SelectedGame is null) return;
        IsBusy = true;
        try
        {
            await _apiClient.SetVisibilityAsync(SelectedGame.Id, !SelectedGame.IsVisible);
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Visibility update failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RemoveSelectedRecordOnlyAsync()
    {
        if (SelectedGame is null) return;
        IsBusy = true;
        try
        {
            string name = SelectedGame.Name;
            await _apiClient.RemoveGameRecordOnlyAsync(SelectedGame.Id);
            SelectedGame = null;
            await RefreshAllAsync();
            StatusBanner = $"Removed '{name}' from database library only. Files on storage share were NOT touched.";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Removal failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task UploadCoverFileAsync(string localPath)
    {
        IsBusy = true;
        try
        {
            string serverRelativePath = await _apiClient.UploadCoverAsync(localPath);
            EditCoverImagePath = serverRelativePath;
            StatusBanner = $"Uploaded cover image to {serverRelativePath}";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Cover upload failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task RunBackupAsync()
    {
        IsBusy = true;
        try
        {
            BackupResult result = await _apiClient.TriggerBackupAsync();
            await RefreshAllAsync();
            StatusBanner = $"Backup created: {result.BackupFilePath} ({result.SizeBytes:N0} bytes)";
        }
        catch (Exception ex)
        {
            StatusBanner = $"Backup failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void LoadEditorFromSelection()
    {
        if (SelectedGame is null) return;
        EditName = SelectedGame.Name;
        EditCategory = SelectedGame.Category;
        EditDeveloper = SelectedGame.Developer;
        EditDescription = SelectedGame.Description;
        EditCoverImagePath = SelectedGame.CoverImagePath;
        EditStorageLocationId = SelectedGame.StorageLocationId;
        EditExecutablePath = SelectedGame.ExecutablePath;
        EditLaunchArguments = SelectedGame.LaunchArguments;
        EditLaunchType = SelectedGame.LaunchType;
        EditExternalAppId = SelectedGame.ExternalAppId;
        EditProcessNameToMonitor = SelectedGame.ProcessNameToMonitor;
        EditIsVisible = SelectedGame.IsVisible;
        EditIsFeatured = SelectedGame.IsFeatured;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
