using System.IO;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CyberHub.ClientLauncher.Services;
using CyberHub.ClientLauncher.ViewModels;
using CyberHub.Shared.Services;

namespace CyberHub.ClientLauncher;

public partial class MainWindow : Window
{
    private readonly CatalogClientService _catalogService;
    private readonly ThemeSyncService _themeSyncService;
    private readonly MainViewModel _viewModel;
    private readonly DispatcherTimer _liveSyncTimer;
    private string _loadedVideoSource = string.Empty;

    public MainWindow()
    {
        InitializeComponent();

        ClientConfig config = ClientConfigLoader.Load();
        _catalogService = new CatalogClientService(config);
        _themeSyncService = new ThemeSyncService(config);
        var prefsService = new LocalPreferencesService();
        var processTracker = new WindowsProcessTracker();
        var sessionManager = new LocalGameSessionManager(processTracker);
        var launchPaths = new ClientLaunchPaths
        {
            SteamExecutablePath = config.SteamExecutablePath,
            RiotClientPath = config.RiotClientPath
        };
        var launchService = new GameLaunchService(launchPaths, sessionManager, processTracker);

        _viewModel = new MainViewModel(
            _catalogService,
            _themeSyncService,
            prefsService,
            launchService,
            minimizeWindow: () => Dispatcher.Invoke(() => WindowState = WindowState.Minimized),
            restoreWindow: () => Dispatcher.Invoke(() =>
            {
                WindowState = WindowState.Maximized;
                Activate();
                Focus();
            }));

        _viewModel.VideoPlaybackStateChanged += (_, shouldPlay) =>
            Dispatcher.Invoke(() => ApplyVideoPlaybackState(shouldPlay));

        _viewModel.AppIconChanged += (_, iconPath) =>
            Dispatcher.Invoke(() => ApplyWindowIcon(iconPath));

        DataContext = _viewModel;

        _liveSyncTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(30)
        };
        _liveSyncTimer.Tick += async (_, _) => await _viewModel.SyncThemeInBackgroundAsync();

        Loaded += async (_, _) =>
        {
            await _viewModel.InitializeAsync();
            ApplyVideoPlaybackState(_viewModel.ShouldPlayVideoBackground);
            ApplyWindowIcon(_viewModel.ResolvedAppIconUri);
            _liveSyncTimer.Start();
        };
    }

    private void ApplyVideoPlaybackState(bool shouldPlay)
    {
        try
        {
            if (!shouldPlay)
            {
                BackgroundVideoPlayer.Stop();
                return;
            }

            string target = _viewModel.ResolvedBackgroundVideoUri;
            if (!string.IsNullOrWhiteSpace(target) &&
                !string.Equals(_loadedVideoSource, target, StringComparison.OrdinalIgnoreCase))
            {
                _loadedVideoSource = target;
                BackgroundVideoPlayer.Source = new Uri(target, UriKind.RelativeOrAbsolute);
            }

            BackgroundVideoPlayer.Play();
        }
        catch (Exception)
        {
            // Keep UI stable using static fallback background image
        }
    }

    private void ApplyWindowIcon(string iconPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(iconPath) || (!iconPath.StartsWith("http", StringComparison.OrdinalIgnoreCase) && !File.Exists(iconPath)))
            {
                return;
            }

            Icon = BitmapFrame.Create(new Uri(iconPath, UriKind.RelativeOrAbsolute));
        }
        catch (Exception)
        {
            // Ignore invalid custom icon cleanly
        }
    }

    private void BackgroundVideoPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.ShouldPlayVideoBackground)
        {
            BackgroundVideoPlayer.Position = TimeSpan.Zero;
            BackgroundVideoPlayer.Play();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        _liveSyncTimer.Stop();
        _themeSyncService.Dispose();
        _catalogService.Dispose();
        base.OnClosed(e);
    }
}
