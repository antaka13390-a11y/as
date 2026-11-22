using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using CyberHub.AdminPanel.Localization;
using CyberHub.AdminPanel.Services;
using CyberHub.Shared.Models;

namespace CyberHub.AdminPanel.ViewModels;

public sealed class SelectableCategoryItem : INotifyPropertyChanged
{
    private bool _isSelected;
    public required CategoryEntry Category { get; init; }
    public required AdminLocalizationService Loc { get; init; }

    public string DisplayName => Loc.IsArabic ? Category.NameAr : Category.NameEn;

    public bool IsSelected
    {
        get => _isSelected;
        set { _isSelected = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected))); }
    }

    public void NotifyLanguageChanged() =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DisplayName)));

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class AdminViewModel : INotifyPropertyChanged
{
    private readonly AdminApiClient _apiClient;
    private GameEntry? _selectedGame;
    private CategoryEntry? _selectedCategory;
    private string _statusBanner = string.Empty;
    private bool _isBusy;

    // Game editor fields
    private string _editName = string.Empty;
    private NetworkMode _editNetworkMode = NetworkMode.Online;
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

    // Category editor fields
    private string _catNameEn = string.Empty;
    private string _catNameAr = string.Empty;
    private string _catIconKey = "gamepad";
    private string _catBannerImagePath = string.Empty;
    private int _catDisplayOrder = 10;

    // Station recovery fields
    private string _recoveryStationName = "PC-01";
    private string _issuedRecoveryCode = string.Empty;

    // Security & Account fields
    private string _passwordStrengthLabel = "Weak (0/5)";
    private string _securityMessage = string.Empty;
    private bool _securityError;

    public event PropertyChangedEventHandler? PropertyChanged;
    public event EventHandler? ReloginRequested;

    public AdminLocalizationService Loc { get; }
    public ThemeEditorViewModel ThemeEditor { get; }
    public GameScannerViewModel Scanner { get; }
    public ObservableCollection<GameEntry> Games { get; } = new();
    public ObservableCollection<StorageLocation> StorageLocations { get; } = new();
    public ObservableCollection<CategoryEntry> Categories { get; } = new();
    public ObservableCollection<SelectableCategoryItem> AssignableCategories { get; } = new();
    public ObservableCollection<AdminAuditLog> AuditLogs { get; } = new();

    public Array LaunchTypes => Enum.GetValues(typeof(LaunchType));
    public Array NetworkModes => Enum.GetValues(typeof(NetworkMode));
    public string[] AvailableIcons { get; } = { "gamepad", "flame", "crosshair", "trophy", "gauge", "compass", "swords", "globe" };

    public AdminViewModel(AdminApiClient apiClient, AdminLocalizationService loc)
    {
        _apiClient = apiClient;
        Loc = loc;
        ThemeEditor = new ThemeEditorViewModel(apiClient, loc, msg => StatusBanner = msg);
        Scanner = new GameScannerViewModel(apiClient, loc, msg => StatusBanner = msg, RefreshAllAsync);

        Loc.PropertyChanged += (_, _) =>
        {
            foreach (SelectableCategoryItem item in AssignableCategories)
            {
                item.NotifyLanguageChanged();
            }
        };
    }

    public GameEntry? SelectedGame
    {
        get => _selectedGame;
        set { _selectedGame = value; OnPropertyChanged(); LoadEditorFromSelection(); }
    }

    public CategoryEntry? SelectedCategory
    {
        get => _selectedCategory;
        set { _selectedCategory = value; OnPropertyChanged(); LoadCategoryEditorFromSelection(); }
    }

    public string StatusBanner { get => _statusBanner; set { _statusBanner = value; OnPropertyChanged(); } }
    public bool IsBusy { get => _isBusy; set { _isBusy = value; OnPropertyChanged(); } }

    public string EditName { get => _editName; set { _editName = value; OnPropertyChanged(); } }
    public NetworkMode EditNetworkMode { get => _editNetworkMode; set { _editNetworkMode = value; OnPropertyChanged(); } }
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

    public string CatNameEn { get => _catNameEn; set { _catNameEn = value; OnPropertyChanged(); } }
    public string CatNameAr { get => _catNameAr; set { _catNameAr = value; OnPropertyChanged(); } }
    public string CatIconKey { get => _catIconKey; set { _catIconKey = value; OnPropertyChanged(); } }
    public string CatBannerImagePath { get => _catBannerImagePath; set { _catBannerImagePath = value; OnPropertyChanged(); } }
    public int CatDisplayOrder { get => _catDisplayOrder; set { _catDisplayOrder = value; OnPropertyChanged(); } }

    public string RecoveryStationName { get => _recoveryStationName; set { _recoveryStationName = value; OnPropertyChanged(); } }
    public string IssuedRecoveryCode { get => _issuedRecoveryCode; set { _issuedRecoveryCode = value; OnPropertyChanged(); } }
    public string PasswordStrengthLabel { get => _passwordStrengthLabel; set { _passwordStrengthLabel = value; OnPropertyChanged(); } }
    public string SecurityMessage { get => _securityMessage; set { _securityMessage = value; OnPropertyChanged(); } }
    public bool SecurityError { get => _securityError; set { _securityError = value; OnPropertyChanged(); } }

    public async Task RefreshAllAsync()
    {
        IsBusy = true;
        try
        {
            List<StorageLocation> locations = await _apiClient.GetStorageLocationsAsync();
            StorageLocations.Clear();
            foreach (StorageLocation loc in locations) StorageLocations.Add(loc);
            Scanner.SyncStorageLocations(locations);

            List<CategoryEntry> categories = await _apiClient.GetCategoriesAsync();
            Categories.Clear();
            AssignableCategories.Clear();
            foreach (CategoryEntry cat in categories)
            {
                Categories.Add(cat);
                if (!cat.IsSystem)
                {
                    AssignableCategories.Add(new SelectableCategoryItem { Category = cat, Loc = Loc });
                }
            }

            List<GameEntry> games = await _apiClient.GetGamesAsync();
            Games.Clear();
            foreach (GameEntry g in games) Games.Add(g);

            List<AdminAuditLog> logs = await _apiClient.GetLogsAsync();
            AuditLogs.Clear();
            foreach (AdminAuditLog log in logs) AuditLogs.Add(log);

            await ThemeEditor.LoadAsync();

            SyncSelectedCategoryCheckboxes();
            StatusBanner = $"Synchronized {Games.Count} games, {Categories.Count} categories, {ThemeEditor.SavedThemes.Count} themes, {AuditLogs.Count} logs.";
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
        EditNetworkMode = NetworkMode.Online;
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
        foreach (SelectableCategoryItem item in AssignableCategories)
        {
            item.IsSelected = false;
        }
    }

    public async Task SaveGameAsync()
    {
        IsBusy = true;
        try
        {
            List<string> selectedCategoryIds = AssignableCategories
                .Where(c => c.IsSelected)
                .Select(c => c.Category.Id)
                .ToList();

            var req = new UpsertGameRequest
            {
                Name = EditName,
                CategoryIds = selectedCategoryIds,
                Category = AssignableCategories.FirstOrDefault(c => c.IsSelected)?.Category.NameEn ?? "Action",
                NetworkMode = EditNetworkMode,
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
            }
            else
            {
                await _apiClient.UpdateGameAsync(SelectedGame.Id, req);
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

    public void BeginNewCategory()
    {
        SelectedCategory = null;
        CatNameEn = string.Empty;
        CatNameAr = string.Empty;
        CatIconKey = "gamepad";
        CatBannerImagePath = string.Empty;
        CatDisplayOrder = Categories.Count + 1;
    }

    public async Task SaveCategoryAsync()
    {
        IsBusy = true;
        try
        {
            var req = new UpsertCategoryRequest
            {
                NameEn = CatNameEn,
                NameAr = CatNameAr,
                IconKey = CatIconKey,
                BannerImagePath = CatBannerImagePath,
                DisplayOrder = CatDisplayOrder
            };

            if (SelectedCategory is null)
            {
                await _apiClient.CreateCategoryAsync(req);
            }
            else
            {
                await _apiClient.UpdateCategoryAsync(SelectedCategory.Id, req);
            }

            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Category save failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task DeleteSelectedCategorySafelyAsync()
    {
        if (SelectedCategory is null) return;
        IsBusy = true;
        try
        {
            await _apiClient.DeleteCategorySafelyAsync(SelectedCategory.Id);
            SelectedCategory = null;
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Category delete failed: {ex.Message}";
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
            await _apiClient.RemoveGameRecordOnlyAsync(SelectedGame.Id);
            SelectedGame = null;
            await RefreshAllAsync();
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
            EditCoverImagePath = await _apiClient.UploadCoverAsync(localPath);
            StatusBanner = $"Uploaded cover image to {EditCoverImagePath}";
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

    public async Task GenerateRecoveryCodeAsync()
    {
        IsBusy = true;
        try
        {
            RecoveryCodeResponse resp = await _apiClient.IssueRecoveryCodeAsync(RecoveryStationName);
            IssuedRecoveryCode = $"{resp.RecoveryCode} (Expires UTC: {resp.ExpiresAtUtc:HH:mm:ss})";
            await RefreshAllAsync();
        }
        catch (Exception ex)
        {
            StatusBanner = $"Recovery code error: {ex.Message}";
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
        EditNetworkMode = SelectedGame.NetworkMode;
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
        SyncSelectedCategoryCheckboxes();
    }

    private void SyncSelectedCategoryCheckboxes()
    {
        HashSet<string> assigned = SelectedGame?.CategoryIds.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new();
        foreach (SelectableCategoryItem item in AssignableCategories)
        {
            item.IsSelected = assigned.Contains(item.Category.Id);
        }
    }

    private void LoadCategoryEditorFromSelection()
    {
        if (SelectedCategory is null) return;
        CatNameEn = SelectedCategory.NameEn;
        CatNameAr = SelectedCategory.NameAr;
        CatIconKey = SelectedCategory.IconKey;
        CatBannerImagePath = SelectedCategory.BannerImagePath;
        CatDisplayOrder = SelectedCategory.DisplayOrder;
    }

    public void UpdatePasswordStrengthIndicator(string password)
    {
        int score = 0;
        if (!string.IsNullOrEmpty(password))
        {
            if (password.Length >= 10) score++;
            if (password.Length >= 14) score++;
            if (password.Any(char.IsUpper) && password.Any(char.IsLower)) score++;
            if (password.Any(char.IsDigit)) score++;
            if (password.Any(c => !char.IsLetterOrDigit(c))) score++;
        }

        PasswordStrengthLabel = score switch
        {
            >= 5 => Loc.IsArabic ? "قوية جداً (5/5)" : "Very Strong (5/5)",
            4 => Loc.IsArabic ? "قوية (4/5)" : "Strong (4/5)",
            3 => Loc.IsArabic ? "متوسطة (3/5)" : "Fair (3/5)",
            _ => Loc.IsArabic ? $"ضعيفة ({score}/5)" : $"Weak ({score}/5)"
        };
    }

    public async Task ChangePasswordAsync(string currentPwd, string newPwd, string confirmPwd)
    {
        SecurityMessage = string.Empty;
        SecurityError = false;

        if (!string.Equals(newPwd, confirmPwd, StringComparison.Ordinal))
        {
            SecurityError = true;
            SecurityMessage = Loc["Security.PasswordMismatch"];
            return;
        }

        IsBusy = true;
        try
        {
            ChangePasswordResponse res = await _apiClient.ChangePasswordAsync(currentPwd, newPwd, confirmPwd);
            SecurityError = false;
            SecurityMessage = Loc["Security.PasswordChanged"];
            StatusBanner = SecurityMessage;
            if (res.RequiresRelogin)
            {
                ReloginRequested?.Invoke(this, EventArgs.Empty);
            }
        }
        catch (Exception ex)
        {
            SecurityError = true;
            SecurityMessage = ex.Message;
            StatusBanner = $"Password change failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task SecureLogoutAsync()
    {
        await _apiClient.LogoutAsync();
        ReloginRequested?.Invoke(this, EventArgs.Empty);
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
