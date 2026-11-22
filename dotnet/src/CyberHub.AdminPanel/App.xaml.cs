using System.Windows;
using CyberHub.AdminPanel.Services;
using CyberHub.AdminPanel.Views;

namespace CyberHub.AdminPanel;

public partial class App : Application
{
    private readonly AdminApiClient _apiClient = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var loginWindow = new LoginWindow(_apiClient);
        bool? authenticated = loginWindow.ShowDialog();

        if (authenticated == true && _apiClient.IsAuthenticated)
        {
            var mainWindow = new AdminMainWindow(_apiClient);
            ShutdownMode = ShutdownMode.OnMainWindowClose;
            MainWindow = mainWindow;
            mainWindow.Show();
        }
        else
        {
            _apiClient.Dispose();
            Shutdown();
        }
    }
}
