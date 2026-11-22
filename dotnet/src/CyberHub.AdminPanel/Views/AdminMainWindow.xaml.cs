using System.Windows;
using CyberHub.AdminPanel.Localization;
using CyberHub.AdminPanel.Services;
using CyberHub.AdminPanel.ViewModels;
using Microsoft.Win32;

namespace CyberHub.AdminPanel.Views;

public partial class AdminMainWindow : Window
{
    private readonly AdminApiClient _apiClient;
    private readonly AdminViewModel _viewModel;

    public event EventHandler? ReloginRequested;

    public AdminMainWindow(AdminApiClient apiClient, AdminLocalizationService loc)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _viewModel = new AdminViewModel(apiClient, loc);
        _viewModel.ReloginRequested += (_, _) =>
        {
            Dispatcher.Invoke(() =>
            {
                CurrentPasswordBox.Clear();
                NewPasswordBox.Clear();
                ConfirmPasswordBox.Clear();
                ReloginRequested?.Invoke(this, EventArgs.Empty);
            });
        };

        DataContext = _viewModel;
        Loaded += async (_, _) => await _viewModel.RefreshAllAsync();
    }

    private void ToggleLang_Click(object sender, RoutedEventArgs e) =>
        _viewModel.Loc.ToggleLanguage();

    private async void Refresh_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.RefreshAllAsync();

    private void NewGame_Click(object sender, RoutedEventArgs e) =>
        _viewModel.BeginNewGame();

    private async void SaveGame_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.SaveGameAsync();

    private async void ToggleVisibility_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.ToggleSelectedVisibilityAsync();

    private async void RemoveRecord_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedGame is null) return;

        MessageBoxResult confirm = MessageBox.Show(
            $"Remove '{_viewModel.SelectedGame.Name}' from the CyberHub launcher database?\n\nActual game folders and executables on CCBoot / SMB shares will NOT be deleted.",
            "Confirm Library Record Removal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            await _viewModel.RemoveSelectedRecordOnlyAsync();
        }
    }

    private void NewCategory_Click(object sender, RoutedEventArgs e) =>
        _viewModel.BeginNewCategory();

    private async void SaveCategory_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.SaveCategoryAsync();

    private async void DeleteCategory_Click(object sender, RoutedEventArgs e)
    {
        if (_viewModel.SelectedCategory is null) return;

        MessageBoxResult confirm = MessageBox.Show(
            $"Delete category '{_viewModel.SelectedCategory.NameEn}'? Games assigned to this category will NOT be deleted.",
            "Confirm Safe Category Removal",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);

        if (confirm == MessageBoxResult.Yes)
        {
            await _viewModel.DeleteSelectedCategorySafelyAsync();
        }
    }

    private async void UploadCover_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Image Files (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp"
        };

        if (dialog.ShowDialog() == true)
        {
            await _viewModel.UploadCoverFileAsync(dialog.FileName);
        }
    }

    private async void BackupDb_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.RunBackupAsync();

    private async void IssueRecovery_Click(object sender, RoutedEventArgs e) =>
        await _viewModel.GenerateRecoveryCodeAsync();

    private void NewPasswordBox_PasswordChanged(object sender, RoutedEventArgs e) =>
        _viewModel.UpdatePasswordStrengthIndicator(NewPasswordBox.Password);

    private async void ChangePassword_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.ChangePasswordAsync(
            CurrentPasswordBox.Password,
            NewPasswordBox.Password,
            ConfirmPasswordBox.Password);
    }

    private async void SecureLogout_Click(object sender, RoutedEventArgs e)
    {
        await _viewModel.SecureLogoutAsync();
    }
}
