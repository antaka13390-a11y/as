using System.Windows;
using CyberHub.AdminPanel.Localization;
using CyberHub.AdminPanel.Services;
using CyberHub.Shared.Models;

namespace CyberHub.AdminPanel.Views;

public partial class LoginWindow : Window
{
    private readonly AdminApiClient _apiClient;
    private readonly AdminLocalizationService _loc;
    private bool _needsInitialSetup;

    public LoginWindow(AdminApiClient apiClient, AdminLocalizationService loc)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _loc = loc;
        DataContext = _loc;
        Loaded += LoginWindow_Loaded;
    }

    private async void LoginWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            AdminStatusResponse status = await _apiClient.GetStatusAsync();
            _needsInitialSetup = !status.IsConfigured;
            SubtitleText.Text = $"{status.DetectedWorkstation} ({status.DetectedClientIp})";
            SubmitButton.Content = _needsInitialSetup ? _loc["Admin.ClaimPassword"] : _loc["Admin.SignIn"];
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            SubmitButton.IsEnabled = false;
        }
    }

    private void ToggleLang_Click(object sender, RoutedEventArgs e)
    {
        _loc.ToggleLanguage();
        SubmitButton.Content = _needsInitialSetup ? _loc["Admin.ClaimPassword"] : _loc["Admin.SignIn"];
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
