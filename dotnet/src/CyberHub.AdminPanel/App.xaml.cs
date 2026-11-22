using System.Windows;
using CyberHub.AdminPanel.Localization;
using CyberHub.AdminPanel.Services;
using CyberHub.AdminPanel.Views;

namespace CyberHub.AdminPanel;

public partial class App : Application
{
    private readonly AdminApiClient _apiClient = new();
    private readonly AdminLocalizationService _loc = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        ShowLoginAndMainFlow();
    }

    private void ShowLoginAndMainFlow()
    {
        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        var loginWindow = new LoginWindow(_apiClient, _loc);
        bool? authenticated = loginWindow.ShowDialog();

        if (authenticated == true && _apiClient.IsAuthenticated)
        {
            var mainWindow = new AdminMainWindow(_apiClient, _loc);
            bool reloginRequested = false;

            mainWindow.ReloginRequested += (_, _) =>
            {
                reloginRequested = true;
                ShutdownMode = ShutdownMode.OnExplicitShutdown;
                _apiClient.ClearSession();
                mainWindow.Close();
                ShowLoginAndMainFlow();
            };

            mainWindow.Closed += (_, _) =>
            {
                if (!reloginRequested)
                {
                    _apiClient.Dispose();
                    Shutdown();
                }
            };

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
