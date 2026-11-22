using System.Collections.ObjectModel;
using System.Windows.Input;
using CyberHub.ClientLauncher.Services;
using CyberHub.Shared.Models;

namespace CyberHub.ClientLauncher.ViewModels;

public enum LauncherSection
{
    Home,
    AllGames,
    Favorites,
    RecentlyPlayed
}

public sealed class GameCardItem : ObservableObject
{
    private bool _isFavorite;
    private DateTimeOffset? _lastPlayedUtc;

    public required GameEntry Entry { get; init; }
    public required string ResolvedCoverUri { get; init; }
    public required string ResolvedHeroUri { get; init; }

    public bool IsFavorite
    {
        get => _isFavorite;
        set => SetProperty(ref _isFavorite, value);
    }

    public DateTimeOffset? LastPlayedUtc
    {
        get => _lastPlayedUtc;
        set => SetProperty(ref _lastPlayedUtc, value);
    }
}

public sealed class MainViewModel : ObservableObject
{
    private readonly CatalogClientService _catalogService;
    private readonly LocalPreferencesService _prefsService;
    private readonly GameLaunchService _launchService;
    private readonly Action _minimizeWindow;
    private readonly Action _restoreWindow;

    private List<GameCardItem> _allGames = new();
    private PlayerPreferences _preferences = new();
    private LauncherSection _activeSection = LauncherSection.Home;
    private string _selectedCategory = "All";
    private string _searchQuery = string.Empty;
    private GameCardItem? _selectedGame;
    private GameCardItem? _featuredGame;
    private string _statusMessage = string.Empty;
    private string _catalogSourceLabel = "Connecting...";
    private bool _isOfflineWarning;
    private bool _isLaunching;

    public ObservableCollection<GameCardItem> FilteredGames { get; } = new();
    public ObservableCollection<string> Categories { get; } = new() { "All" };

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

    public string SectionTitle => ActiveSection switch
    {
        LauncherSection.Home => "Featured & Quick Launch",
        LauncherSection.AllGames => "All Games Library",
        LauncherSection.Favorites => "Station Favorites",
        LauncherSection.RecentlyPlayed => "Recently Played",
        _ => "Library"
    };

    public string SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (SetProperty(ref _selectedCategory, value))
            {
                ApplyFilter();
            }
        }
    }

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
        set => SetProperty(ref _selectedGame, value);
    }

    public GameCardItem? FeaturedGame
    {
        get => _featuredGame;
        set => SetProperty(ref _featuredGame, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => SetProperty(ref _statusMessage, value);
    }

    public string CatalogSourceLabel
    {
        get => _catalogSourceLabel;
        set => SetProperty(ref _catalogSourceLabel, value);
    }

    public bool IsOfflineWarning
    {
        get => _isOfflineWarning;
        set => SetProperty(ref _isOfflineWarning, value);
    }

    public bool IsLaunching
    {
        get => _isLaunching;
        set => SetProperty(ref _isLaunching, value);
    }

    public ICommand NavigateCommand { get; }
    public ICommand SelectCategoryCommand { get; }
    public ICommand SelectGameCommand { get; }
    public ICommand CloseDetailsCommand { get; }
    public ICommand ToggleFavoriteCommand { get; }
    public ICommand PlayGameCommand { get; }
    public ICommand RefreshCatalogCommand { get; }

    public MainViewModel(
        CatalogClientService catalogService,
        LocalPreferencesService prefsService,
        GameLaunchService launchService,
        Action minimizeWindow,
        Action restoreWindow)
    {
        _catalogService = catalogService;
        _prefsService = prefsService;
        _launchService = launchService;
        _minimizeWindow = minimizeWindow;
        _restoreWindow = restoreWindow;

        NavigateCommand = new RelayCommand(param =>
        {
            if (param is string sectionName && Enum.TryParse<LauncherSection>(sectionName, out LauncherSection parsed))
            {
                ActiveSection = parsed;
            }
        });

        SelectCategoryCommand = new RelayCommand(param =>
        {
            if (param is string cat)
            {
                SelectedCategory = cat;
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

        ToggleFavoriteCommand = new RelayCommand(param =>
        {
            if (param is GameCardItem card)
            {
                ToggleFavorite(card);
            }
        });

        PlayGameCommand = new RelayCommand(async param =>
        {
            GameCardItem? target = param as GameCardItem ?? SelectedGame ?? FeaturedGame;
            if (target is not null)
            {
                await LaunchGameAsync(target);
            }
        }, _ => !IsLaunching);

        RefreshCatalogCommand = new RelayCommand(async _ => await InitializeAsync());
    }

    public async Task InitializeAsync()
    {
        _preferences = _prefsService.Load();
        CatalogFetchResult result = await _catalogService.LoadCatalogAsync();

        CatalogSourceLabel = result.SourceLabel;
        IsOfflineWarning = result.IsOfflineFallback;
        StatusMessage = result.WarningMessage ?? string.Empty;

        _allGames = result.Snapshot.Games
            .Where(g => g.IsVisible)
            .Select(g =>
            {
                RecentPlayEntry? recent = _preferences.RecentlyPlayed.FirstOrDefault(r => r.GameId == g.Id);
                return new GameCardItem
                {
                    Entry = g,
                    ResolvedCoverUri = _catalogService.ResolveImageUri(g.CoverImagePath),
                    ResolvedHeroUri = _catalogService.ResolveImageUri(string.IsNullOrWhiteSpace(g.HeroImagePath) ? g.CoverImagePath : g.HeroImagePath),
                    IsFavorite = _preferences.FavoriteGameIds.Contains(g.Id),
                    LastPlayedUtc = recent?.LastPlayedUtc
                };
            })
            .ToList();

        Categories.Clear();
        Categories.Add("All");
        foreach (string cat in _allGames.Select(g => g.Entry.Category).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c))
        {
            Categories.Add(cat);
        }

        FeaturedGame = _allGames.FirstOrDefault(g => g.Entry.IsFeatured) ?? _allGames.FirstOrDefault();
        ApplyFilter();
    }

    private void ApplyFilter()
    {
        IEnumerable<GameCardItem> query = _allGames;

        switch (ActiveSection)
        {
            case LauncherSection.Favorites:
                query = query.Where(g => g.IsFavorite);
                break;
            case LauncherSection.RecentlyPlayed:
                query = query.Where(g => g.LastPlayedUtc.HasValue)
                             .OrderByDescending(g => g.LastPlayedUtc);
                break;
            case LauncherSection.Home:
                query = query.OrderByDescending(g => g.Entry.IsFeatured)
                             .ThenBy(g => g.Entry.SortOrder);
                break;
        }

        if (!string.Equals(SelectedCategory, "All", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(g => string.Equals(g.Entry.Category, SelectedCategory, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            string q = SearchQuery.Trim();
            query = query.Where(g =>
                g.Entry.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.Entry.Category.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                g.Entry.Developer.Contains(q, StringComparison.OrdinalIgnoreCase));
        }

        FilteredGames.Clear();
        foreach (GameCardItem item in query)
        {
            FilteredGames.Add(item);
        }
    }

    private void ToggleFavorite(GameCardItem card)
    {
        if (card.IsFavorite)
        {
            _preferences.FavoriteGameIds.Remove(card.Entry.Id);
            card.IsFavorite = false;
        }
        else
        {
            _preferences.FavoriteGameIds.Add(card.Entry.Id);
            card.IsFavorite = true;
        }

        _prefsService.Save(_preferences);
        if (ActiveSection == LauncherSection.Favorites)
        {
            ApplyFilter();
        }
    }

    private async Task LaunchGameAsync(GameCardItem card)
    {
        if (IsLaunching)
        {
            return;
        }

        IsLaunching = true;
        StatusMessage = $"Validating path & starting {card.Entry.Name}...";

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

        IsLaunching = false;
        StatusMessage = outcome.Message;
    }
}
