using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using CyberHub.ClientLauncher.Localization;
using CyberHub.ClientLauncher.Services;
using CyberHub.Shared.Models;
using CyberHub.Shared.Services;

namespace CyberHub.ClientLauncher.ViewModels;

public enum LauncherSection
{
    Home,
    RecentlyPlayed,
    Category
}

public sealed class CategoryNavItem : ObservableObject
{
    public required CategoryEntry Entry { get; init; }
    private readonly LocalizationService _loc;

    public CategoryNavItem(LocalizationService loc)
    {
        _loc = loc;
        _loc.PropertyChanged += (_, _) => OnPropertyChanged(nameof(DisplayName));
    }

    public string DisplayName => _loc.IsArabic ? Entry.NameAr : Entry.NameEn;
}

public sealed class GameCardItem : ObservableObject
{
    private DateTimeOffset? _lastPlayedUtc;
    private readonly LocalizationService _loc;

    public GameCardItem(LocalizationService loc)
    {
        _loc = loc;
        _loc.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(NetworkModeLabel));
            OnPropertyChanged(nameof(CategorySummary));
        };
    }

    public required GameEntry Entry { get; init; }
    public required string ResolvedCoverUri { get; init; }
    public required string ResolvedHeroUri { get; init; }
    public List<CategoryEntry> AssignedCategories { get; init; } = new();

    public string NetworkModeLabel => Entry.NetworkMode == NetworkMode.Online
        ? _loc["Network.Online"]
        : _loc["Network.Offline"];

    public string CategorySummary => AssignedCategories.Count > 0
        ? string.Join(" · ", AssignedCategories.Select(c => _loc.IsArabic ? c.NameAr : c.NameEn))
        : Entry.Category;

    public DateTimeOffset? LastPlayedUtc
    {
        get => _lastPlayedUtc;
        set => SetProperty(ref _lastPlayedUtc, value);
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly CatalogClientService _catalogService;
    private readonly ThemeSyncService _themeSyncService;
    private readonly LocalPreferencesService _prefsService;
    private readonly GameLaunchService _launchService;
    private readonly Action _minimizeWindow;
    private readonly Action _restoreWindow;

    private List<GameCardItem> _allGames = new();
    private PlayerPreferences _preferences = new();
    private LauncherSection _activeSection = LauncherSection.Home;
    private CategoryNavItem? _selectedCategory;
    private string _searchQuery = string.Empty;
    private GameCardItem? _selectedGame;
    private GameCardItem? _featuredGame;
    private string _statusMessage = string.Empty;
    private LocalGameSessionState? _activeSession;
    private string _recoveryCodeInput = string.Empty;
    private string _recoveryError = string.Empty;
    private bool _isLoadingCatalog = true;
    private ThemeConfig _activeTheme;

    public LocalizationService Loc { get; }
    public ObservableCollection<GameCardItem> FilteredGames { get; } = new();
    public ObservableCollection<CategoryNavItem> DynamicCategories { get; } = new();

    public event EventHandler<bool>? VideoPlaybackStateChanged;
    public event EventHandler<string>? AppIconChanged;

    public ThemeConfig ActiveTheme
    {
        get => _activeTheme;
        private set
        {
            if (SetProperty(ref _activeTheme, value))
            {
                RaiseThemePropertiesChanged();
            }
        }
    }

    public bool IsLoadingCatalog
    {
        get => _isLoadingCatalog;
        private set => SetProperty(ref _isLoadingCatalog, value);
    }

    // Dynamic Branding (AR & EN)
    public string AppDisplayName => Loc.IsArabic ? ActiveTheme.ApplicationNameAr : ActiveTheme.ApplicationNameEn;
    public string CafeDisplayName => Loc.IsArabic ? ActiveTheme.CyberCafeNameAr : ActiveTheme.CyberCafeNameEn;
    public string WindowDisplayTitle => Loc.IsArabic ? ActiveTheme.WindowTitleAr : ActiveTheme.WindowTitleEn;
    public string LoadingDisplayTitle => Loc.IsArabic ? ActiveTheme.LoadingScreenTitleAr : ActiveTheme.LoadingScreenTitleEn;
    public string WelcomeDisplayTitle => Loc.IsArabic ? ActiveTheme.WelcomeTitleAr : ActiveTheme.WelcomeTitleEn;

    // Resolved Asset URIs (Local Cache First -> Server Fallback)
    public string ResolvedLogoUri => _themeSyncService.ResolveAssetUri(ActiveTheme.LogoAssetPath);
    public string ResolvedAppIconUri => _themeSyncService.ResolveAssetUri(ActiveTheme.AppIconAssetPath);
    public string ResolvedLoadingLogoUri => _themeSyncService.ResolveAssetUri(
        string.IsNullOrWhiteSpace(ActiveTheme.LoadingLogoAssetPath) ? ActiveTheme.LogoAssetPath : ActiveTheme.LoadingLogoAssetPath);
    public string ResolvedBackgroundImageUri => _themeSyncService.ResolveAssetUri(
        !string.IsNullOrWhiteSpace(ActiveTheme.BackgroundImageAssetPath)
            ? ActiveTheme.BackgroundImageAssetPath
            : ActiveTheme.StaticFallbackImagePath);
    public string ResolvedBackgroundVideoUri => _themeSyncService.ResolveAssetUri(ActiveTheme.BackgroundVideoAssetPath);

    public bool HasCustomLogo => !string.IsNullOrWhiteSpace(ResolvedLogoUri);
    public bool HasLoadingLogo => !string.IsNullOrWhiteSpace(ResolvedLoadingLogoUri);
    public bool ShowBackgroundImageLayer =>
        (ActiveTheme.BackgroundType == ThemeBackgroundType.Image ||
         (ActiveTheme.BackgroundType == ThemeBackgroundType.Video && (!ShouldPlayVideoBackground || !string.IsNullOrWhiteSpace(ActiveTheme.StaticFallbackImagePath)))) &&
        !string.IsNullOrWhiteSpace(ResolvedBackgroundImageUri);

    public bool ShouldPlayVideoBackground =>
        ActiveTheme.BackgroundType == ThemeBackgroundType.Video &&
        ActiveTheme.VideoBackgroundEnabled &&
        !ActiveTheme.PerformanceFriendlyMode &&
        !IsSessionLocked &&
        !string.IsNullOrWhiteSpace(ResolvedBackgroundVideoUri);

    // Dynamic Theme Brushes & Visuals
    public Brush WindowBackgroundBrush => BuildWindowBackgroundBrush();
    public Brush SidebarBackgroundBrush => ParseBrush(ActiveTheme.SidebarBackgroundColor, "#0F1118");
    public Brush CardBackgroundBrush => ParseBrush(ActiveTheme.GameCardBackgroundColor, "#11131C");
    public Brush PrimaryAccentBrush => ParseBrush(ActiveTheme.PrimaryAccentColor, "#8B5CF6");
    public Brush SecondaryAccentBrush => ParseBrush(ActiveTheme.SecondaryAccentColor, "#06B6D4");
    public Brush PrimaryTextBrush => ParseBrush(ActiveTheme.PrimaryTextColor, "#F8FAFC");
    public Brush SecondaryTextBrush => ParseBrush(ActiveTheme.SecondaryTextColor, "#94A3B8");
    public Brush ButtonBackgroundBrush => ParseBrush(ActiveTheme.ButtonBackgroundColor, "#8B5CF6");
    public Brush ButtonTextBrush => ParseBrush(ActiveTheme.ButtonTextColor, "#FFFFFF");
    public Brush BorderBrushDynamic => ParseBrush(ActiveTheme.BorderColor, "#252A3D");
    public Brush OverlayScrimBrush => new SolidColorBrush(Color.FromArgb(
        (byte)Math.Clamp(ActiveTheme.OverlayOpacity * 255, 0, 240), 6, 7, 11));

    public Stretch BackgroundStretchMode => ActiveTheme.ImageScaling == BackgroundImageScaling.Contain
        ? Stretch.Uniform
        : Stretch.UniformToFill;

    public double BackgroundMediaOpacity => Math.Clamp(ActiveTheme.BackgroundOpacity * ActiveTheme.BackgroundBrightness, 0.05, 1.0);
    public double BackgroundBlurRadius => ActiveTheme.PerformanceFriendlyMode ? 0 : Math.Clamp(ActiveTheme.BlurIntensity, 0, 40);

    // Dynamic Card & Layout Dimensions
    public double CardWidth => Math.Clamp(ActiveTheme.CardWidth, 160, 420);
    public double CardHeight => Math.Clamp(ActiveTheme.CardHeight, 220, 560);
    public double CoverImageHeight => Math.Clamp(ActiveTheme.CoverImageHeight, 120, CardHeight - 48);
    public double TitleFontSize => Math.Clamp(ActiveTheme.TitleFontSize, 12, 26);
    public bool ShowDescriptionOnCard => ActiveTheme.ShowDescriptionOnCard;
    public int GridColumns => Math.Clamp(ActiveTheme.GridColumns, 2, 7);
    public CornerRadius CardCornerRadius => ActiveTheme.CardShape == CardShapeStyle.Sharp
        ? new CornerRadius(2)
        : new CornerRadius(Math.Clamp(ActiveTheme.CardBorderRadius, 0, 28));
    public Thickness CardMargin => new(0, 0, Math.Clamp(ActiveTheme.CardSpacing, 8, 40), Math.Clamp(ActiveTheme.CardSpacing, 8, 40));
    public Thickness MainContentMargin => new(Math.Clamp(ActiveTheme.MainContentSpacing, 12, 60));
    public double LogoSizePx => Math.Clamp(ActiveTheme.LogoSizePx, 20, 96);

    public GridLength SidebarLeftColumnWidth => ResolveSidebarOnRight()
        ? new GridLength(1, GridUnitType.Star)
        : new GridLength(Math.Clamp(ActiveTheme.SidebarWidth, 180, 380));
    public GridLength SidebarRightColumnWidth => ResolveSidebarOnRight()
        ? new GridLength(Math.Clamp(ActiveTheme.SidebarWidth, 180, 380))
        : new GridLength(1, GridUnitType.Star);
    public int SidebarGridColumn => ResolveSidebarOnRight() ? 1 : 0;
    public int ContentGridColumn => ResolveSidebarOnRight() ? 0 : 1;

    private bool ResolveSidebarOnRight() => ActiveTheme.SidebarPosition switch
    {
        SidebarPosition.Left => Loc.IsArabic,   // In RTL Grid, Column 1 is visual Left
        SidebarPosition.Right => !Loc.IsArabic, // In RTL Grid, Column 0 is visual Right
        _ => false                              // SidebarPosition.Start follows Window FlowDirection at Grid.Column=0
    };

    public LauncherSection ActiveSection
    {
        get => _activeSection;
        set
        {
            if (SetProperty(ref _activeSection, value))
            {
                ApplyFilter();
                OnPropertyChanged(nameof(SectionTitle));
            }
        }
    }

    public CategoryNavItem? SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                if (value is not null)
                {
                    _activeSection = LauncherSection.Category;
                    OnPropertyChanged(nameof(ActiveSection));
                }
                ApplyFilter();
                OnPropertyChanged(nameof(SectionTitle));
            }
        }
    }

    public string SectionTitle => ActiveSection switch
    {
        LauncherSection.Home => Loc["Section.HomeTitle"],
        LauncherSection.RecentlyPlayed => Loc["Section.RecentTitle"],
        LauncherSection.Category when SelectedCategory is not null => SelectedCategory.DisplayName,
        _ => Loc["Nav.AllGames"]
    };

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                ApplyFilter();
            }
        }
    }

    public GameCardItem? SelectedGame
    {
        get => _selectedGame;
        set
        {
            if (SetProperty(ref _selectedGame, value))
            {
                OnPropertyChanged(nameof(HasSelectedGame));
            }
        }
    }

    public bool HasSelectedGame => SelectedGame is not null;

    public GameCardItem? FeaturedGame
    {
        get => _featuredGame;
        set
        {
            if (SetProperty(ref _featuredGame, value))
            {
                OnPropertyChanged(nameof(HasFeaturedGame));
            }
        }
    }

    public bool HasFeaturedGame => FeaturedGame is not null;

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(StatusMessage);

    public LocalGameSessionState? ActiveSession
    {
        get => _activeSession;
        private set
        {
            if (SetProperty(ref _activeSession, value))
            {
                OnPropertyChanged(nameof(IsSessionLocked));
                OnPropertyChanged(nameof(CanPlayGames));
                OnPropertyChanged(nameof(RequiresAdminRecovery));
                OnPropertyChanged(nameof(ActiveSessionBannerText));
                OnPropertyChanged(nameof(ShouldPlayVideoBackground));
                OnPropertyChanged(nameof(ShowBackgroundImageLayer));
                VideoPlaybackStateChanged?.Invoke(this, ShouldPlayVideoBackground);
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public bool IsSessionLocked => ActiveSession is not null;
    public bool CanPlayGames => ActiveSession is null;
    public bool RequiresAdminRecovery => ActiveSession?.RequiresAdminRecovery == true;

    public string ActiveSessionBannerText => ActiveSession is null
        ? string.Empty
        : $"{Loc["Session.InProgressTitle"]} — {Loc["Session.ActivePrefix"]} {ActiveSession.GameName}";

    public string RecoveryCodeInput
    {
        get => _recoveryCodeInput;
        set => SetProperty(ref _recoveryCodeInput, value);
    }

    public string RecoveryError
    {
        get => _recoveryError;
        set => SetProperty(ref _recoveryError, value);
    }

    public ICommand NavigateHomeCommand { get; }
    public ICommand NavigateRecentCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand SelectGameCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand PlayGameCommand { get; }
    public ICommand RefreshCatalogCommand { get; }
    public ICommand ToggleLanguageCommand { get; }
    public ICommand SubmitRecoveryUnlockCommand { get; }

    public MainViewModel(
        CatalogClientService catalogService,
        ThemeSyncService themeSyncService,
        LocalPreferencesService prefsService,
        GameLaunchService launchService,
        Action minimizeWindow,
        Action restoreWindow)
    {
        _catalogService = catalogService;
        _themeSyncService = themeSyncService;
        _prefsService = prefsService;
        _launchService = launchService;
        _minimizeWindow = minimizeWindow;
        _restoreWindow = restoreWindow;

        _activeTheme = _themeSyncService.GetInitialCachedTheme().Payload.Theme;

        _preferences = _prefsService.Load();
        Loc = new LocalizationService(_preferences.PreferredLanguage);
        Loc.PropertyChanged += (_, _) =>
        {
            OnPropertyChanged(nameof(SectionTitle));
            OnPropertyChanged(nameof(ActiveSessionBannerText));
            OnPropertyChanged(nameof(AppDisplayName));
            OnPropertyChanged(nameof(CafeDisplayName));
            OnPropertyChanged(nameof(WindowDisplayTitle));
            OnPropertyChanged(nameof(LoadingDisplayTitle));
            OnPropertyChanged(nameof(WelcomeDisplayTitle));
            OnPropertyChanged(nameof(SidebarLeftColumnWidth));
            OnPropertyChanged(nameof(SidebarRightColumnWidth));
            OnPropertyChanged(nameof(SidebarGridColumn));
            OnPropertyChanged(nameof(ContentGridColumn));
        };

        _themeSyncService.ThemeUpdated += (_, syncResult) =>
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                ActiveTheme = syncResult.Payload.Theme;
            });
        };

        _activeSession = _launchService.SessionManager.CurrentSession;
        _launchService.SessionManager.SessionChanged += (_, session) =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is not null && !dispatcher.CheckAccess())
            {
                dispatcher.Invoke(() => ActiveSession = session);
            }
            else
            {
                ActiveSession = session;
            }
        };

        NavigateHomeCommand = new RelayCommand(_ =>
        {
            _selectedCategory = null;
            OnPropertyChanged(nameof(SelectedCategory));
            ActiveSection = LauncherSection.Home;
        });

        NavigateRecentCommand = new RelayCommand(_ =>
        {
            _selectedCategory = null;
            OnPropertyChanged(nameof(SelectedCategory));
            ActiveSection = LauncherSection.RecentlyPlayed;
        });

        SelectCategoryCommand = new RelayCommand(param =>
        {
            if (param is CategoryNavItem item)
            {
                SelectedCategory = item;
            }
        });

        SelectGameCommand = new RelayCommand(param =>
        {
            if (param is GameCardItem card)
            {
                SelectedGame = card;
            }
        });

        CloseDetailsCommand = new RelayCommand(_ => SelectedGame = null);

        PlayGameCommand = new RelayCommand(async param =>
        {
            GameCardItem? target = param as GameCardItem ?? SelectedGame ?? FeaturedGame;
            if (target is not null)
            {
                await LaunchGameAsync(target);
            }
        }, _ => CanPlayGames);

        RefreshCatalogCommand = new RelayCommand(async _ => await InitializeAsync());

        ToggleLanguageCommand = new RelayCommand(_ =>
        {
            _preferences.PreferredLanguage = Loc.ToggleLanguage();
            _prefsService.Save(_preferences);
        });

        SubmitRecoveryUnlockCommand = new RelayCommand(async _ =>
        {
            RecoveryError = string.Empty;
            bool ok = await _launchService.SessionManager.TryAdminRecoveryUnlockAsync(
                RecoveryCodeInput,
                (station, code) => _catalogService.VerifyStationRecoveryCodeAsync(station, code));

            if (!ok)
            {
                RecoveryError = Loc["Session.InvalidRecoveryCode"];
                return;
            }

            RecoveryCodeInput = string.Empty;
            StatusMessage = Loc["Session.Completed"];
        });
    }

    public async Task InitializeAsync()
    {
        IsLoadingCatalog = true;
        try
        {
            _preferences = _prefsService.Load();
            Loc.SetLanguage(_preferences.PreferredLanguage);

            ThemeSyncResult themeResult = await _themeSyncService.SyncPublishedThemeAsync();
            ActiveTheme = themeResult.Payload.Theme;

            CatalogFetchResult result = await _catalogService.LoadCatalogAsync();
            StatusMessage = result.WarningKey is not null ? Loc[result.WarningKey] : string.Empty;

            DynamicCategories.Clear();
            foreach (CategoryEntry cat in result.Snapshot.Categories.OrderBy(c => c.DisplayOrder))
            {
                DynamicCategories.Add(new CategoryNavItem(Loc) { Entry = cat });
            }

            _allGames = result.Snapshot.Games
                .Where(g => g.IsVisible)
                .Select(g =>
                {
                    RecentPlayEntry? recent = _preferences.RecentlyPlayed.FirstOrDefault(r => r.GameId == g.Id);
                    List<CategoryEntry> matchedCats = result.Snapshot.Categories
                        .Where(c => !c.IsSystem && g.CategoryIds.Contains(c.Id))
                        .ToList();

                    return new GameCardItem(Loc)
                    {
                        Entry = g,
                        ResolvedCoverUri = _catalogService.ResolveImageUri(g.CoverImagePath),
                        ResolvedHeroUri = _catalogService.ResolveImageUri(string.IsNullOrWhiteSpace(g.HeroImagePath) ? g.CoverImagePath : g.HeroImagePath),
                        AssignedCategories = matchedCats,
                        LastPlayedUtc = recent?.LastPlayedUtc
                    };
                })
                .ToList();

            FeaturedGame = _allGames.FirstOrDefault(g => g.Entry.IsFeatured) ?? _allGames.FirstOrDefault();
            ApplyFilter();
        }
        finally
        {
            IsLoadingCatalog = false;
        }
    }

    public Task SyncThemeInBackgroundAsync() => _themeSyncService.SyncPublishedThemeAsync();

    private void RaiseThemePropertiesChanged()
    {
        OnPropertyChanged(nameof(AppDisplayName));
        OnPropertyChanged(nameof(CafeDisplayName));
        OnPropertyChanged(nameof(WindowDisplayTitle));
        OnPropertyChanged(nameof(LoadingDisplayTitle));
        OnPropertyChanged(nameof(WelcomeDisplayTitle));
        OnPropertyChanged(nameof(ResolvedLogoUri));
        OnPropertyChanged(nameof(ResolvedAppIconUri));
        OnPropertyChanged(nameof(ResolvedLoadingLogoUri));
        OnPropertyChanged(nameof(ResolvedBackgroundImageUri));
        OnPropertyChanged(nameof(ResolvedBackgroundVideoUri));
        OnPropertyChanged(nameof(HasCustomLogo));
        OnPropertyChanged(nameof(HasLoadingLogo));
        OnPropertyChanged(nameof(ShowBackgroundImageLayer));
        OnPropertyChanged(nameof(ShouldPlayVideoBackground));
        OnPropertyChanged(nameof(WindowBackgroundBrush));
        OnPropertyChanged(nameof(SidebarBackgroundBrush));
        OnPropertyChanged(nameof(CardBackgroundBrush));
        OnPropertyChanged(nameof(PrimaryAccentBrush));
        OnPropertyChanged(nameof(SecondaryAccentBrush));
        OnPropertyChanged(nameof(PrimaryTextBrush));
        OnPropertyChanged(nameof(SecondaryTextBrush));
        OnPropertyChanged(nameof(ButtonBackgroundBrush));
        OnPropertyChanged(nameof(ButtonTextBrush));
        OnPropertyChanged(nameof(BorderBrushDynamic));
        OnPropertyChanged(nameof(OverlayScrimBrush));
        OnPropertyChanged(nameof(BackgroundStretchMode));
        OnPropertyChanged(nameof(BackgroundMediaOpacity));
        OnPropertyChanged(nameof(BackgroundBlurRadius));
        OnPropertyChanged(nameof(CardWidth));
        OnPropertyChanged(nameof(CardHeight));
        OnPropertyChanged(nameof(CoverImageHeight));
        OnPropertyChanged(nameof(TitleFontSize));
        OnPropertyChanged(nameof(ShowDescriptionOnCard));
        OnPropertyChanged(nameof(GridColumns));
        OnPropertyChanged(nameof(CardCornerRadius));
        OnPropertyChanged(nameof(CardMargin));
        OnPropertyChanged(nameof(MainContentMargin));
        OnPropertyChanged(nameof(LogoSizePx));
        OnPropertyChanged(nameof(SidebarLeftColumnWidth));
        OnPropertyChanged(nameof(SidebarRightColumnWidth));
        OnPropertyChanged(nameof(SidebarGridColumn));
        OnPropertyChanged(nameof(ContentGridColumn));

        VideoPlaybackStateChanged?.Invoke(this, ShouldPlayVideoBackground);
        AppIconChanged?.Invoke(this, ResolvedAppIconUri);
    }

    private Brush BuildWindowBackgroundBrush()
    {
        if (ActiveTheme.BackgroundType == ThemeBackgroundType.Solid)
        {
            return ParseBrush(ActiveTheme.SolidBackgroundColor, "#08090D");
        }

        Color start = ParseColor(ActiveTheme.GradientStartColor, "#08090D");
        Color end = ParseColor(ActiveTheme.GradientEndColor, "#19102E");
        double rad = ActiveTheme.GradientAngleDegrees * Math.PI / 180.0;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5 - 0.5 * Math.Cos(rad), 0.5 - 0.5 * Math.Sin(rad)),
            EndPoint = new Point(0.5 + 0.5 * Math.Cos(rad), 0.5 + 0.5 * Math.Sin(rad))
        };
        brush.GradientStops.Add(new GradientStop(start, 0));
        brush.GradientStops.Add(new GradientStop(end, 1));
        return brush;
    }

    private static Color ParseColor(string? hex, string fallbackHex)
    {
        try
        {
            object? converted = ColorConverter.ConvertFromString(hex);
            if (converted is Color c)
            {
                return c;
            }
        }
        catch (Exception)
        {
            // Fallback below
        }

        return (Color)ColorConverter.ConvertFromString(fallbackHex);
    }

    private static Brush ParseBrush(string? hex, string fallbackHex)
    {
        return new SolidColorBrush(ParseColor(hex, fallbackHex));
    }

    private void ApplyFilter()
    {
        IEnumerable<GameCardItem> query = _allGames;

        switch (ActiveSection)
        {
            case LauncherSection.RecentlyPlayed:
                query = query.Where(g => g.LastPlayedUtc.HasValue)
                             .OrderByDescending(g => g.LastPlayedUtc);
                break;
            case LauncherSection.Category when SelectedCategory is not null:
                query = SelectedCategory.Entry.FilterType switch
                {
                    CategoryFilterType.AllGames => query,
                    CategoryFilterType.OnlineGames => query.Where(g => g.Entry.NetworkMode == NetworkMode.Online),
                    CategoryFilterType.OfflineGames => query.Where(g => g.Entry.NetworkMode == NetworkMode.Offline),
                    _ => query.Where(g => g.Entry.CategoryIds.Contains(SelectedCategory.Entry.Id))
                };
                break;
            case LauncherSection.Home:
                query = query.OrderByDescending(g => g.Entry.IsFeatured)
                             .ThenBy(g => g.Entry.SortOrder);
                break;
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            query = query.Where(g =>
                g.Entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.CategorySummary.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.Entry.Developer.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        FilteredGames.Clear();
        foreach (GameCardItem item in query)
        {
            FilteredGames.Add(item);
        }
    }

    private async Task LaunchGameAsync(GameCardItem card)
    {
        StatusMessage = Loc["Session.Validating"];

        _preferences.RecentlyPlayed.RemoveAll(r => r.GameId == card.Entry.Id);
        var recent = new RecentPlayEntry
        {
            GameId = card.Entry.Id,
            LastPlayedUtc = DateTimeOffset.UtcNow
        };
        _preferences.RecentlyPlayed.Insert(0, recent);
        card.LastPlayedUtc = recent.LastPlayedUtc;
        _prefsService.Save(_preferences);

        LaunchOutcome outcome = await _launchService.LaunchGameAsync(
            card.Entry,
            _minimizeWindow,
            _restoreWindow);

        StatusMessage = outcome.MessageKey switch
        {
            "Session.DuplicateBlocked" => Loc["Session.DuplicateBlocked"],
            "Session.RecoveryRequired" => Loc["Session.RecoveryRequired"],
            "Session.Completed" => Loc["Session.Completed"],
            _ => outcome.MessageDetail
        };
    }
}
