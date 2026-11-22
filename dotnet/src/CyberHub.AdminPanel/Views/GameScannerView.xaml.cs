using System.Windows;
using System.Windows.Controls;
using CyberHub.AdminPanel.ViewModels;
using CyberHub.Shared.Models;
using Microsoft.Win32;

namespace CyberHub.AdminPanel.Views;

public partial class GameScannerView : UserControl
{
    public GameScannerView()
    {
        InitializeComponent();
    }

    private AdminViewModel? Vm => DataContext as AdminViewModel;

    private async void AddStorage_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.Scanner.AddStorageLocationAsync();
    }

    private async void DeleteStorage_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null && sender is FrameworkElement el && el.Tag is SelectableStorageLocationItem item)
        {
            await Vm.Scanner.DeleteStorageLocationAsync(item);
        }
    }

    private async void StartScan_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.Scanner.StartBackgroundScanAsync(Vm.Categories.ToList());
    }

    private async void CancelScan_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.Scanner.CancelActiveScanAsync();
    }

    private void IgnoreCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is DetectedCandidateItem candidate)
        {
            candidate.IsIgnored = true;
            candidate.IsSelected = false;
        }
    }

    private async void SearchArtwork_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.Scanner.SearchCoverArtForSelectedAsync();
    }

    private async void UploadArtwork_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.Scanner.SelectedCandidate is null) return;
        var dialog = new OpenFileDialog
        {
            Filter = "Artwork Images (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp"
        };
        if (dialog.ShowDialog() == true)
        {
            await Vm.Scanner.UploadManualCoverForSelectedAsync(dialog.FileName);
        }
    }

    private void RemoveArtwork_Click(object sender, RoutedEventArgs e) =>
        Vm?.Scanner.RemoveCoverFromSelected();

    private void SelectArtworkCandidate_Click(object sender, RoutedEventArgs e)
    {
        if (Vm?.Scanner.SelectedCandidate is not null && sender is FrameworkElement el && el.Tag is ArtworkCandidate art)
        {
            Vm.Scanner.SelectedCandidate.SelectedCoverUrl = art.ImageUrl;
            Vm.Scanner.SelectedCandidate.Source.SelectedCoverUrl = art.ImageUrl;
            Vm.Scanner.SelectedCandidate.Source.Metadata.HeroImageUrl = art.HeroUrl;
        }
    }

    private async void ImportSelected_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null) return;
        int count = Vm.Scanner.Candidates.Count(c => c.IsSelected && !c.IsIgnored);
        if (count == 0)
        {
            MessageBox.Show("Select at least one detected game before importing.", "Import Games", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        MessageBoxResult confirm = MessageBox.Show(
            $"Import {count} selected game(s) into the CYBER HUB central library?\n\nExisting library entries will NOT be overwritten and original game files remain untouched.",
            "Confirm Game Import",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirm == MessageBoxResult.Yes)
        {
            await Vm.Scanner.ImportSelectedCandidatesAsync();
        }
    }
}
