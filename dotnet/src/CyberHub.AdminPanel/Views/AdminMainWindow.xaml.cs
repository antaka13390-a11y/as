using System.Windows;
using CyberHub.AdminPanel.Services;
using CyberHub.AdminPanel.ViewModels;
using Microsoft.Win32;

namespace CyberHub.AdminPanel.Views;

public partial class AdminMainWindow : Window
{
    private readonly AdminApiClient _apiClient;
    private readonly AdminViewModel _viewModel;

    public AdminMainWindow(AdminApiClient apiClient)
    {
        InitializeComponent();
        _apiClient = apiClient;
        _viewModel = new AdminViewModel(apiClient);
        DataContext = _viewModel;

        Loaded += async (_, _) => await _viewModel.RefreshAllAsync();
    }

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

    protected override void OnClosed(EventArgs e)
    {
        _apiClient.Dispose();
        base.OnClosed(e);
    }
}
