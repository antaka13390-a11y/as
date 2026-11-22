using System.Windows;
using CyberHub.AdminPanel.Services;
using CyberHub.Shared.Models;

namespace CyberHub.AdminPanel.Views;

public partial class LoginWindow : Window
{
    private readonly AdminApiClient _apiClient;
    private bool _needsInitialSetup;

    public LoginWindow(AdminApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        Loaded += LoginWindow_Loaded;
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AdminStatusResponse status = await _apiClient.GetStatusAsync();
            _needsInitialSetup = !status.IsConfigured;
            SubtitleText.Text = _needsInitialSetup
                ? $"First-run setup on {status.DetectedWorkstation} ({status.DetectedClientIp}). Choose an admin password (min 10 chars)."
                : $"Authorized workstation: {status.DetectedWorkstation} ({status.DetectedClientIp}).";
            SubmitButton.Content = _needsInitialSetup ? "Claim Admin Password" : "Sign In";
        }
        catch (Exception ex)
        {
            SubtitleText.Text = "Connection or authorization blocked.";
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            SubmitButton.IsEnabled = false;
        }
    }

    private async void SubmitButton_Click(object sender, RoutedEventArgs e)
    {
        ErrorText.Visibility = Visibility.Collapsed;
        SubmitButton.IsEnabled = false;

        try
        {
            string pwd = PasswordInput.Password;
            if (_needsInitialSetup)
            {
                await _apiClient.SetupInitialAdminAsync(pwd);
            }
            else
            {
                await _apiClient.LoginAsync(pwd);
            }

            DialogResult = true;
            Close();
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            SubmitButton.IsEnabled = true;
        }
    }
}
