using System.Threading;
using System.Windows;

namespace CyberHub.ClientLauncher;

public partial class App : Application
{
    private Mutex? _singleInstanceMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        _singleInstanceMutex = new Mutex(true, @"Global\CyberHubClientLauncherMutex", out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show("CYBER HUB is already running on this station.", "CYBER HUB", MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        base.OnStartup(e);
        var window = new MainWindow();
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _singleInstanceMutex?.ReleaseMutex();
        _singleInstanceMutex?.Dispose();
        base.OnExit(e);
    }
}
