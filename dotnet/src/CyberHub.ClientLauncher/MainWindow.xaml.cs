using System.Windows;
using CyberHub.ClientLauncher.Services;
using CyberHub.ClientLauncher.ViewModels;

namespace CyberHub.ClientLauncher;

public partial class MainWindow : Window
{
    private readonly CatalogClientService _catalogService;
    private readonly MainViewModel _viewModel;

    public MainWindow()
    {
        InitializeComponent();

        ClientConfig config = ClientConfigLoader.Load();
        _catalogService = new CatalogClientService(config);
        var prefsService = new LocalPreferencesService();
        var launchService = new GameLaunchService(config);

        _viewModel = new MainViewModel(
            _catalogService,
            prefsService,
            launchService,
            minimizeWindow: () => Dispatcher.Invoke(() => WindowState = WindowState.Minimized),
            restoreWindow: () => Dispatcher.Invoke(() =>
            {
                WindowState = WindowState.Maximized;
                Activate();
                Focus();
            }));

        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    protected override void OnClosed(EventArgs e)
    {
        _catalogService.Dispose();
        base.OnClosed(e);
    }
}
