using System.Windows;
using System.Windows.Controls;
using CyberHub.AdminPanel.ViewModels;
using Microsoft.Win32;

namespace CyberHub.AdminPanel.Views;

public partial class AppearanceThemeView : UserControl
{
    public AppearanceThemeView()
    {
        InitializeComponent();
    }

    private ThemeEditorViewModel? Vm => DataContext as ThemeEditorViewModel;

    private void DraftText_Changed(object sender, TextChangedEventArgs e) => Vm?.NotifyPreviewChanged();
    private void DraftValue_Changed(object sender, SelectionChangedEventArgs e) => Vm?.NotifyPreviewChanged();
    private void DraftCheck_Changed(object sender, RoutedEventArgs e) => Vm?.NotifyPreviewChanged();

    private void PreviewDraft_Click(object sender, RoutedEventArgs e) => Vm?.NotifyPreviewChanged();
    private void PreviewAr_Click(object sender, RoutedEventArgs e) { if (Vm is not null) Vm.PreviewLanguage = "ar"; }
    private void PreviewEn_Click(object sender, RoutedEventArgs e) { if (Vm is not null) Vm.PreviewLanguage = "en"; }
    private void ResetChanges_Click(object sender, RoutedEventArgs e) => Vm?.ResetChanges();
    private void RestoreOriginalBranding_Click(object sender, RoutedEventArgs e) => Vm?.RestoreOriginalBranding();

    private void ApplyPreset_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement el && el.Tag is string presetKey)
        {
            Vm?.ApplyPresetToDraft(presetKey);
        }
    }

    private async void SaveDraft_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.SaveDraftAsync();
    }

    private async void PublishTheme_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.PublishDraftAsync();
    }

    private async void RestoreDefault_Click(object sender, RoutedEventArgs e)
    {
        MessageBoxResult confirm = MessageBox.Show(
            "Restore factory CYBER HUB theme and branding on the server and all CCBoot clients?",
            "Restore Default Theme",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        if (confirm == MessageBoxResult.Yes && Vm is not null)
        {
            await Vm.RestoreDefaultThemeAsync();
        }
    }

    private async void CreateTheme_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.CreateCustomThemeAsync();
    }

    private async void RenameTheme_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.RenameSelectedAsync();
    }

    private async void DuplicateTheme_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.DuplicateSelectedAsync();
    }

    private async void SetDefaultTheme_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.SetDefaultSelectedAsync();
    }

    private async void DeleteTheme_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is not null) await Vm.DeleteSelectedAsync();
    }

    private async void UploadAssetSlot_Click(object sender, RoutedEventArgs e)
    {
        if (Vm is null || sender is not FrameworkElement el || el.Tag is not string slot) return;

        string filter = slot == "bgVideo"
            ? "MP4 Video (*.mp4)|*.mp4"
            : slot == "appIcon"
                ? "Icon & Image Files (*.ico;*.png;*.jpg;*.webp)|*.ico;*.png;*.jpg;*.jpeg;*.webp"
                : "Image Files (*.png;*.jpg;*.jpeg;*.webp)|*.png;*.jpg;*.jpeg;*.webp";

        var dialog = new OpenFileDialog { Filter = filter };
        if (dialog.ShowDialog() == true)
        {
            await Vm.UploadAssetForSlotAsync(dialog.FileName, slot);
        }
    }
}
