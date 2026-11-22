using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Media;
using CyberHub.AdminPanel.Localization;
using CyberHub.AdminPanel.Services;
using CyberHub.Shared.Models;
using CyberHub.Shared.Services;

namespace CyberHub.AdminPanel.ViewModels;

public sealed class ThemeEditorViewModel : INotifyPropertyChanged
{
    private readonly AdminApiClient _apiClient;
    private readonly Action<string> _setStatus;
    private ThemeConfig? _selectedTheme;
    private ThemeConfig _draft = ThemePresetFactory.CreateNeonPurple(isDefault: true, isPublished: true);
    private string _previewLanguage = "ar";
    private long _publishedVersion = 1;
    private string _newThemeName = "My Cafe Theme";
    private string _renameInput = string.Empty;
    private bool _hasUnsavedChanges;

    public event PropertyChangedEventHandler? PropertyChanged;

    public AdminLocalizationService Loc { get; }
    public ObservableCollection<ThemeConfig> SavedThemes { get; } = new();
    public ObservableCollection<ThemeAssetMetadata> Assets { get; } = new();
    public ObservableCollection<ThemeAuditLog> ThemeLogs { get; } = new();

    public Array BackgroundTypes => Enum.GetValues(typeof(ThemeBackgroundType));
    public Array ImageScalings => Enum.GetValues(typeof(BackgroundImageScaling));
    public Array CardLayoutStyles => Enum.GetValues(typeof(CardLayoutStyle));
    public Array CardShapes => Enum.GetValues(typeof(CardShapeStyle));
    public Array HoverAnimations => Enum.GetValues(typeof(CardHoverAnimation));
    public Array BorderEffects => Enum.GetValues(typeof(CardBorderEffect));
    public Array PlayButtonStyles => Enum.GetValues(typeof(PlayButtonStyle));
    public Array SidebarPositions => Enum.GetValues(typeof(SidebarPosition));
    public Array NavigationStyles => Enum.GetValues(typeof(NavigationStyle));
    public Array LogoPositions => Enum.GetValues(typeof(LogoPosition));

    public string[] PresetKeys { get; } =
    {
        "NeonPurple",
        "CyberBlue",
        "DarkRed",
        "LuxuryGold",
        "MinimalDark",
        "Custom"
    };

    public ThemeEditorViewModel(AdminApiClient apiClient, AdminLocalizationService loc, Action<string> setStatus)
    {
        _apiClient = apiClient;
        Loc = loc;
        _setStatus = setStatus;
    }

    public ThemeConfig? SelectedTheme
    {
        get => _selectedTheme;
        set
        {
            if (!Equals(_selectedTheme, value))
            {
                _selectedTheme = value;
                OnPropertyChanged();
                if (value is not null)
                {
                    Draft = value.Clone();
                    RenameInput = value.Name;
                    HasUnsavedChanges = false;
                }
            }
        }
    }

    public ThemeConfig Draft
    {
        get => _draft;
        set
        {
            _draft = value;
            OnPropertyChanged();
            NotifyPreviewChanged();
        }
    }

    public string PreviewLanguage
    {
        get => _previewLanguage;
        set
        {
            if (_previewLanguage != value)
            {
                _previewLanguage = value;
                OnPropertyChanged();
                NotifyPreviewChanged();
            }
        }
    }

    public long PublishedVersion { get => _publishedVersion; set { _publishedVersion = value; OnPropertyChanged(); } }
    public string NewThemeName { get => _newThemeName; set { _newThemeName = value; OnPropertyChanged(); } }
    public string RenameInput { get => _renameInput; set { _renameInput = value; OnPropertyChanged(); } }
    public bool HasUnsavedChanges { get => _hasUnsavedChanges; set { _hasUnsavedChanges = value; OnPropertyChanged(); } }

    // Live Customer Preview Bindings
    public FlowDirection PreviewFlowDirection => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase)
        ? FlowDirection.RightToLeft
        : FlowDirection.LeftToRight;

    public string PreviewAppName => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase)
        ? Draft.ApplicationNameAr
        : Draft.ApplicationNameEn;

    public string PreviewCafeName => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase)
        ? Draft.CyberCafeNameAr
        : Draft.CyberCafeNameEn;

    public string PreviewWelcomeTitle => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase)
        ? Draft.WelcomeTitleAr
        : Draft.WelcomeTitleEn;

    public string PreviewWindowTitle => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase)
        ? Draft.WindowTitleAr
        : Draft.WindowTitleEn;

    public string PreviewHomeLabel => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase) ? "الرئيسية" : "Home";
    public string PreviewOnlineLabel => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase) ? "أونلاين" : "Online";
    public string PreviewPlayLabel => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase) ? "تشغيل الآن" : "Play Now";
    public string PreviewSampleGame1 => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase) ? "بروتوكول فالور" : "Valor Protocol";
    public string PreviewSampleGame2 => string.Equals(PreviewLanguage, "ar", StringComparison.OrdinalIgnoreCase) ? "سباق أبيكس" : "Apex Overdrive";

    public string PreviewLogoUrl => _apiClient.ResolveServerAssetUrl(Draft.LogoAssetPath);
    public string PreviewBgImageUrl => _apiClient.ResolveServerAssetUrl(
        !string.IsNullOrWhiteSpace(Draft.BackgroundImageAssetPath) ? Draft.BackgroundImageAssetPath : Draft.StaticFallbackImagePath);
    public bool HasPreviewLogo => !string.IsNullOrWhiteSpace(PreviewLogoUrl);
    public bool HasPreviewBgImage => !string.IsNullOrWhiteSpace(PreviewBgImageUrl) &&
        (Draft.BackgroundType == ThemeBackgroundType.Image || Draft.BackgroundType == ThemeBackgroundType.Video);

    public Brush PreviewBackgroundBrush => BuildBackgroundBrush();
    public Brush PreviewSidebarBrush => ParseBrush(Draft.SidebarBackgroundColor, "#0F1118");
    public Brush PreviewCardBrush => ParseBrush(Draft.GameCardBackgroundColor, "#11131C");
    public Brush PreviewPrimaryAccentBrush => ParseBrush(Draft.PrimaryAccentColor, "#8B5CF6");
    public Brush PreviewSecondaryAccentBrush => ParseBrush(Draft.SecondaryAccentColor, "#06B6D4");
    public Brush PreviewPrimaryTextBrush => ParseBrush(Draft.PrimaryTextColor, "#F8FAFC");
    public Brush PreviewSecondaryTextBrush => ParseBrush(Draft.SecondaryTextColor, "#94A3B8");
    public Brush PreviewButtonBgBrush => ParseBrush(Draft.ButtonBackgroundColor, "#8B5CF6");
    public Brush PreviewButtonTextBrush => ParseBrush(Draft.ButtonTextColor, "#FFFFFF");
    public Brush PreviewBorderBrush => ParseBrush(Draft.BorderColor, "#252A3D");
    public Brush PreviewActiveNavBrush => ParseBrush(Draft.ActiveNavItemColor, "#2B1E54");
    public Brush PreviewOverlayBrush => new SolidColorBrush(Color.FromArgb(
        (byte)Math.Clamp(Draft.OverlayOpacity * 255, 0, 230), 6, 7, 12));
    public CornerRadius PreviewCardRadius => Draft.CardShape == CardShapeStyle.Sharp
        ? new CornerRadius(2)
        : new CornerRadius(Math.Clamp(Draft.CardBorderRadius, 0, 24));
    public double PreviewBlurRadius => Draft.PerformanceFriendlyMode ? 0 : Math.Clamp(Draft.BlurIntensity, 0, 32);

    public void NotifyPreviewChanged()
    {
        HasUnsavedChanges = true;
        OnPropertyChanged(nameof(PreviewFlowDirection));
        OnPropertyChanged(nameof(PreviewAppName));
        OnPropertyChanged(nameof(PreviewCafeName));
        OnPropertyChanged(nameof(PreviewWelcomeTitle));
        OnPropertyChanged(nameof(PreviewWindowTitle));
        OnPropertyChanged(nameof(PreviewHomeLabel));
        OnPropertyChanged(nameof(PreviewOnlineLabel));
        OnPropertyChanged(nameof(PreviewPlayLabel));
        OnPropertyChanged(nameof(PreviewSampleGame1));
        OnPropertyChanged(nameof(PreviewSampleGame2));
        OnPropertyChanged(nameof(PreviewLogoUrl));
        OnPropertyChanged(nameof(PreviewBgImageUrl));
        OnPropertyChanged(nameof(HasPreviewLogo));
        OnPropertyChanged(nameof(HasPreviewBgImage));
        OnPropertyChanged(nameof(PreviewBackgroundBrush));
        OnPropertyChanged(nameof(PreviewSidebarBrush));
        OnPropertyChanged(nameof(PreviewCardBrush));
        OnPropertyChanged(nameof(PreviewPrimaryAccentBrush));
        OnPropertyChanged(nameof(PreviewSecondaryAccentBrush));
        OnPropertyChanged(nameof(PreviewPrimaryTextBrush));
        OnPropertyChanged(nameof(PreviewSecondaryTextBrush));
        OnPropertyChanged(nameof(PreviewButtonBgBrush));
        OnPropertyChanged(nameof(PreviewButtonTextBrush));
        OnPropertyChanged(nameof(PreviewBorderBrush));
        OnPropertyChanged(nameof(PreviewActiveNavBrush));
        OnPropertyChanged(nameof(PreviewOverlayBrush));
        OnPropertyChanged(nameof(PreviewCardRadius));
        OnPropertyChanged(nameof(PreviewBlurRadius));
    }

    public async Task LoadAsync()
    {
        try
        {
            AdminThemesSnapshotResponse snapshot = await _apiClient.GetThemesSnapshotAsync();
            PublishedVersion = snapshot.PublishedVersion;

            SavedThemes.Clear();
            foreach (ThemeConfig theme in snapshot.Themes)
            {
                SavedThemes.Add(theme);
            }

            Assets.Clear();
            foreach (ThemeAssetMetadata asset in snapshot.Assets)
            {
                Assets.Add(asset);
            }

            ThemeLogs.Clear();
            foreach (ThemeAuditLog log in snapshot.AuditLogs)
            {
                ThemeLogs.Add(log);
            }

            ThemeConfig? current = SavedThemes.FirstOrDefault(t => string.Equals(t.Id, snapshot.ActiveThemeId, StringComparison.OrdinalIgnoreCase))
                ?? SavedThemes.FirstOrDefault();

            if (current is not null)
            {
                _selectedTheme = current;
                OnPropertyChanged(nameof(SelectedTheme));
                Draft = current.Clone();
                RenameInput = current.Name;
                HasUnsavedChanges = false;
            }
        }
        catch (Exception ex)
        {
            _setStatus($"Theme load error: {ex.Message}");
        }
    }

    public void ApplyPresetToDraft(string presetKey)
    {
        ThemeConfig preset = ThemePresetFactory.CreatePresetByKey(presetKey);
        preset.Id = Draft.Id;
        preset.Name = Draft.Name;
        preset.IsDefault = Draft.IsDefault;
        preset.IsPublished = Draft.IsPublished;
        // Preserve custom branding unless explicitly reset
        preset.ApplicationNameEn = Draft.ApplicationNameEn;
        preset.ApplicationNameAr = Draft.ApplicationNameAr;
        preset.CyberCafeNameEn = Draft.CyberCafeNameEn;
        preset.CyberCafeNameAr = Draft.CyberCafeNameAr;
        preset.WelcomeTitleEn = Draft.WelcomeTitleEn;
        preset.WelcomeTitleAr = Draft.WelcomeTitleAr;
        preset.LoadingScreenTitleEn = Draft.LoadingScreenTitleEn;
        preset.LoadingScreenTitleAr = Draft.LoadingScreenTitleAr;
        preset.WindowTitleEn = Draft.WindowTitleEn;
        preset.WindowTitleAr = Draft.WindowTitleAr;
        preset.LogoAssetPath = Draft.LogoAssetPath;
        preset.AppIconAssetPath = Draft.AppIconAssetPath;
        preset.LoadingLogoAssetPath = Draft.LoadingLogoAssetPath;
        preset.LogoSizePx = Draft.LogoSizePx;
        preset.LogoPosition = Draft.LogoPosition;
        Draft = preset;
        _setStatus($"Loaded '{presetKey}' preset into draft preview. Click Publish Theme to push to clients.");
    }

    public void ResetChanges()
    {
        if (SelectedTheme is not null)
        {
            Draft = SelectedTheme.Clone();
            HasUnsavedChanges = false;
            _setStatus("Reverted unsaved draft edits.");
        }
    }

    public void RestoreOriginalBranding()
    {
        Draft.ApplicationNameEn = "CYBER HUB";
        Draft.ApplicationNameAr = "سايبر هب";
        Draft.CyberCafeNameEn = "CYBER HUB ARENA";
        Draft.CyberCafeNameAr = "صالة سايبر هب";
        Draft.WelcomeTitleEn = "Welcome to CYBER HUB";
        Draft.WelcomeTitleAr = "مرحباً بكم في سايبر هب";
        Draft.LoadingScreenTitleEn = "Loading Library...";
        Draft.LoadingScreenTitleAr = "جارٍ تحميل المكتبة...";
        Draft.WindowTitleEn = "CYBER HUB";
        Draft.WindowTitleAr = "سايبر هب";
        Draft.LogoAssetPath = string.Empty;
        Draft.AppIconAssetPath = string.Empty;
        Draft.LoadingLogoAssetPath = string.Empty;
        Draft.LogoSizePx = 40;
        Draft.LogoPosition = LogoPosition.SidebarTop;
        OnPropertyChanged(nameof(Draft));
        NotifyPreviewChanged();
        _setStatus("Restored original CYBER HUB branding in draft. Save or Publish to apply.");
    }

    public async Task SaveDraftAsync()
    {
        try
        {
            ThemeConfig saved = await _apiClient.SaveThemeDraftAsync(Draft.Id, Draft);
            await LoadAsync();
            SelectedTheme = SavedThemes.FirstOrDefault(t => t.Id == saved.Id);
            HasUnsavedChanges = false;
            _setStatus($"Saved draft '{saved.Name}' (v{saved.Version}). Clients are unchanged until Publish Theme.");
        }
        catch (Exception ex)
        {
            _setStatus($"Draft save failed: {ex.Message}");
        }
    }

    public async Task PublishDraftAsync()
    {
        try
        {
            ThemeConfig saved = await _apiClient.SaveThemeDraftAsync(Draft.Id, Draft);
            PublishedThemePayload payload = await _apiClient.PublishThemeAsync(saved.Id);
            await LoadAsync();
            SelectedTheme = SavedThemes.FirstOrDefault(t => t.Id == saved.Id);
            HasUnsavedChanges = false;
            _setStatus($"Published '{payload.Theme.Name}' (v{payload.PublishedVersion}) to all CCBoot client computers.");
        }
        catch (Exception ex)
        {
            _setStatus($"Publish failed: {ex.Message}");
        }
    }

    public async Task CreateCustomThemeAsync()
    {
        try
        {
            ThemeConfig next = Draft.Clone();
            next.Id = string.Empty;
            next.Name = string.IsNullOrWhiteSpace(NewThemeName) ? "Custom Cafe Theme" : NewThemeName.Trim();
            next.PresetKey = "Custom";
            next.IsDefault = false;
            next.IsPublished = false;
            ThemeConfig created = await _apiClient.CreateThemeAsync(next);
            await LoadAsync();
            SelectedTheme = SavedThemes.FirstOrDefault(t => t.Id == created.Id);
            _setStatus($"Created custom theme '{created.Name}'.");
        }
        catch (Exception ex)
        {
            _setStatus($"Create theme failed: {ex.Message}");
        }
    }

    public async Task DuplicateSelectedAsync()
    {
        if (SelectedTheme is null) return;
        try
        {
            ThemeConfig copy = await _apiClient.DuplicateThemeAsync(SelectedTheme.Id, $"{SelectedTheme.Name} Copy");
            await LoadAsync();
            SelectedTheme = SavedThemes.FirstOrDefault(t => t.Id == copy.Id);
            _setStatus($"Duplicated theme as '{copy.Name}'.");
        }
        catch (Exception ex)
        {
            _setStatus($"Duplicate failed: {ex.Message}");
        }
    }

    public async Task RenameSelectedAsync()
    {
        if (SelectedTheme is null || string.IsNullOrWhiteSpace(RenameInput)) return;
        try
        {
            ThemeConfig renamed = await _apiClient.RenameThemeAsync(SelectedTheme.Id, RenameInput.Trim());
            await LoadAsync();
            SelectedTheme = SavedThemes.FirstOrDefault(t => t.Id == renamed.Id);
            _setStatus($"Renamed theme to '{renamed.Name}'.");
        }
        catch (Exception ex)
        {
            _setStatus($"Rename failed: {ex.Message}");
        }
    }

    public async Task SetDefaultSelectedAsync()
    {
        if (SelectedTheme is null) return;
        try
        {
            await _apiClient.SetDefaultThemeAsync(SelectedTheme.Id);
            await LoadAsync();
            _setStatus($"Marked '{SelectedTheme.Name}' as default theme.");
        }
        catch (Exception ex)
        {
            _setStatus($"Set default failed: {ex.Message}");
        }
    }

    public async Task DeleteSelectedAsync()
    {
        if (SelectedTheme is null) return;
        try
        {
            string removedName = SelectedTheme.Name;
            await _apiClient.DeleteThemeAsync(SelectedTheme.Id);
            await LoadAsync();
            _setStatus($"Deleted theme '{removedName}'.");
        }
        catch (Exception ex)
        {
            _setStatus($"Delete blocked: {ex.Message}");
        }
    }

    public async Task RestoreDefaultThemeAsync()
    {
        try
        {
            PublishedThemePayload payload = await _apiClient.RestoreDefaultThemeAsync();
            await LoadAsync();
            HasUnsavedChanges = false;
            _setStatus($"Restored factory default CYBER HUB theme & branding (v{payload.PublishedVersion}).");
        }
        catch (Exception ex)
        {
            _setStatus($"Restore default failed: {ex.Message}");
        }
    }

    public async Task UploadAssetForSlotAsync(string localFilePath, string slot)
    {
        try
        {
            ThemeAssetMetadata uploaded = await _apiClient.UploadThemeAssetAsync(localFilePath);
            switch (slot)
            {
                case "bgImage":
                    Draft.BackgroundImageAssetPath = uploaded.RelativeUrl;
                    break;
                case "bgVideo":
                    Draft.BackgroundVideoAssetPath = uploaded.RelativeUrl;
                    Draft.VideoBackgroundEnabled = true;
                    break;
                case "bgFallback":
                    Draft.StaticFallbackImagePath = uploaded.RelativeUrl;
                    break;
                case "logo":
                    Draft.LogoAssetPath = uploaded.RelativeUrl;
                    break;
                case "appIcon":
                    Draft.AppIconAssetPath = uploaded.RelativeUrl;
                    break;
                case "loadingLogo":
                    Draft.LoadingLogoAssetPath = uploaded.RelativeUrl;
                    break;
            }

            OnPropertyChanged(nameof(Draft));
            NotifyPreviewChanged();
            await LoadAsync();
            _setStatus($"Uploaded {uploaded.MediaKind} asset '{uploaded.OriginalFileName}' ({uploaded.SizeBytes:N0} bytes).");
        }
        catch (Exception ex)
        {
            _setStatus($"Asset upload failed: {ex.Message}");
        }
    }

    private Brush BuildBackgroundBrush()
    {
        if (Draft.BackgroundType == ThemeBackgroundType.Solid)
        {
            return ParseBrush(Draft.SolidBackgroundColor, "#08090D");
        }

        Color start = ParseColor(Draft.GradientStartColor, "#08090D");
        Color end = ParseColor(Draft.GradientEndColor, "#19102E");
        double rad = Draft.GradientAngleDegrees * Math.PI / 180.0;
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0.5 - 0.5 * Math.Cos(rad), 0.5 - 0.5 * Math.Sin(rad)),
            EndPoint = new Point(0.5 + 0.5 * Math.Cos(rad), 0.5 + 0.5 * Math.Sin(rad))
        };
        brush.GradientStops.Add(new GradientStop(start, 0));
        brush.GradientStops.Add(new GradientStop(end, 1));
        return brush;
    }

    private static Color ParseColor(string? hex, string fallbackHex)
    {
        try
        {
            if (ColorConverter.ConvertFromString(hex) is Color c) return c;
        }
        catch (Exception)
        {
            // Fallback below
        }
        return (Color)ColorConverter.ConvertFromString(fallbackHex);
    }

    private static Brush ParseBrush(string? hex, string fallbackHex) =>
        new SolidColorBrush(ParseColor(hex, fallbackHex));

    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
