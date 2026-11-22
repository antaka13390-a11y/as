using System.Text.Json.Serialization;

namespace CyberHub.Shared.Models;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ThemeBackgroundType
{
    Solid = 0,
    Gradient = 1,
    Image = 2,
    Video = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardLayoutStyle
{
    Standard = 0,
    LargeArtwork = 1,
    Compact = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardShapeStyle
{
    Rounded = 0,
    Sharp = 1,
    CutCorner = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardHoverAnimation
{
    ScaleUp = 0,
    Glow = 1,
    Lift = 2,
    None = 3
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CardBorderEffect
{
    Subtle = 0,
    NeonGlow = 1,
    None = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum PlayButtonStyle
{
    Gradient = 0,
    Solid = 1,
    Outline = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum SidebarPosition
{
    Start = 0,
    Left = 1,
    Right = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum NavigationStyle
{
    VerticalRail = 0,
    CompactIcons = 1,
    TopTabs = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum LogoPosition
{
    SidebarTop = 0,
    TopBarStart = 1,
    TopBarCenter = 2
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum BackgroundImageScaling
{
    Cover = 0,
    Contain = 1
}

public sealed class ThemeConfig
{
    public string Id { get; set; } = "theme-neon-purple";
    public string Name { get; set; } = "Neon Purple";
    public string PresetKey { get; set; } = "NeonPurple";
    public bool IsDefault { get; set; }
    public bool IsPublished { get; set; }
    public long Version { get; set; } = 1;
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    // A. Background Customization
    public ThemeBackgroundType BackgroundType { get; set; } = ThemeBackgroundType.Gradient;
    public string BackgroundImageAssetPath { get; set; } = string.Empty;
    public string BackgroundVideoAssetPath { get; set; } = string.Empty;
    public string StaticFallbackImagePath { get; set; } = string.Empty;
    public bool VideoBackgroundEnabled { get; set; }
    public bool PerformanceFriendlyMode { get; set; }
    public string SolidBackgroundColor { get; set; } = "#08090D";
    public string GradientStartColor { get; set; } = "#08090D";
    public string GradientEndColor { get; set; } = "#17112B";
    public double GradientAngleDegrees { get; set; } = 135;
    public double BackgroundBrightness { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 1.0;
    public double OverlayOpacity { get; set; } = 0.55;
    public double BlurIntensity { get; set; } = 0;
    public string ImagePosition { get; set; } = "Center";
    public BackgroundImageScaling ImageScaling { get; set; } = BackgroundImageScaling.Cover;

    // B. Complete Color Customization
    public string PrimaryAccentColor { get; set; } = "#8B5CF6";
    public string SecondaryAccentColor { get; set; } = "#06B6D4";
    public string MainBackgroundColor { get; set; } = "#08090D";
    public string SidebarBackgroundColor { get; set; } = "#0F1118";
    public string GameCardBackgroundColor { get; set; } = "#11131C";
    public string PrimaryTextColor { get; set; } = "#F8FAFC";
    public string SecondaryTextColor { get; set; } = "#94A3B8";
    public string ButtonBackgroundColor { get; set; } = "#8B5CF6";
    public string ButtonTextColor { get; set; } = "#FFFFFF";
    public string HoverStateColor { get; set; } = "#1A1E2E";
    public string BorderColor { get; set; } = "#222638";
    public string ActiveNavItemColor { get; set; } = "#2E1E59";

    // C. Game Card Customization
    public CardLayoutStyle CardLayoutStyle { get; set; } = CardLayoutStyle.Standard;
    public CardShapeStyle CardShape { get; set; } = CardShapeStyle.Rounded;
    public double CardBorderRadius { get; set; } = 12;
    public double CardWidth { get; set; } = 248;
    public double CardHeight { get; set; } = 368;
    public double CoverImageHeight { get; set; } = 276;
    public double CardSpacing { get; set; } = 20;
    public double CardShadowIntensity { get; set; } = 0.35;
    public CardHoverAnimation HoverAnimation { get; set; } = CardHoverAnimation.ScaleUp;
    public CardBorderEffect BorderEffect { get; set; } = CardBorderEffect.Subtle;
    public double TitleFontSize { get; set; } = 16;
    public bool ShowDescriptionOnCard { get; set; }
    public PlayButtonStyle PlayButtonStyle { get; set; } = PlayButtonStyle.Gradient;
    public int GridColumns { get; set; } = 4;

    // D. Launcher Layout Customization
    public SidebarPosition SidebarPosition { get; set; } = SidebarPosition.Start;
    public double SidebarWidth { get; set; } = 264;
    public NavigationStyle NavigationStyle { get; set; } = NavigationStyle.VerticalRail;
    public string TopBarAppearance { get; set; } = "Elevated";
    public string SearchBarStyle { get; set; } = "Rounded";
    public string GameGridLayout { get; set; } = "FeaturedPlusGrid";
    public string CategoryNavLayout { get; set; } = "SidebarList";
    public double HeaderSpacing { get; set; } = 24;
    public double MainContentSpacing { get; set; } = 28;

    // E & PART 10. Branding & Custom Application Name
    public string ApplicationNameEn { get; set; } = "CYBER HUB";
    public string ApplicationNameAr { get; set; } = "سايبر هب";
    public string CyberCafeNameEn { get; set; } = "CYBER HUB ARENA";
    public string CyberCafeNameAr { get; set; } = "صالة سايبر هب";
    public string WelcomeTitleEn { get; set; } = "Welcome to CYBER HUB";
    public string WelcomeTitleAr { get; set; } = "مرحباً بكم في سايبر هب";
    public string LoadingScreenTitleEn { get; set; } = "Loading Library...";
    public string LoadingScreenTitleAr { get; set; } = "جارٍ تحميل المكتبة...";
    public string WindowTitleEn { get; set; } = "CYBER HUB";
    public string WindowTitleAr { get; set; } = "سايبر هب";
    public string LogoAssetPath { get; set; } = string.Empty;
    public string AppIconAssetPath { get; set; } = string.Empty;
    public string LoadingLogoAssetPath { get; set; } = string.Empty;
    public double LogoSizePx { get; set; } = 40;
    public LogoPosition LogoPosition { get; set; } = LogoPosition.SidebarTop;

    public ThemeConfig Clone() => new()
    {
        Id = Id,
        Name = Name,
        PresetKey = PresetKey,
        IsDefault = IsDefault,
        IsPublished = IsPublished,
        Version = Version,
        UpdatedAtUtc = UpdatedAtUtc,
        BackgroundType = BackgroundType,
        BackgroundImageAssetPath = BackgroundImageAssetPath,
        BackgroundVideoAssetPath = BackgroundVideoAssetPath,
        StaticFallbackImagePath = StaticFallbackImagePath,
        VideoBackgroundEnabled = VideoBackgroundEnabled,
        PerformanceFriendlyMode = PerformanceFriendlyMode,
        SolidBackgroundColor = SolidBackgroundColor,
        GradientStartColor = GradientStartColor,
        GradientEndColor = GradientEndColor,
        GradientAngleDegrees = GradientAngleDegrees,
        BackgroundBrightness = BackgroundBrightness,
        BackgroundOpacity = BackgroundOpacity,
        OverlayOpacity = OverlayOpacity,
        BlurIntensity = BlurIntensity,
        ImagePosition = ImagePosition,
        ImageScaling = ImageScaling,
        PrimaryAccentColor = PrimaryAccentColor,
        SecondaryAccentColor = SecondaryAccentColor,
        MainBackgroundColor = MainBackgroundColor,
        SidebarBackgroundColor = SidebarBackgroundColor,
        GameCardBackgroundColor = GameCardBackgroundColor,
        PrimaryTextColor = PrimaryTextColor,
        SecondaryTextColor = SecondaryTextColor,
        ButtonBackgroundColor = ButtonBackgroundColor,
        ButtonTextColor = ButtonTextColor,
        HoverStateColor = HoverStateColor,
        BorderColor = BorderColor,
        ActiveNavItemColor = ActiveNavItemColor,
        CardLayoutStyle = CardLayoutStyle,
        CardShape = CardShape,
        CardBorderRadius = CardBorderRadius,
        CardWidth = CardWidth,
        CardHeight = CardHeight,
        CoverImageHeight = CoverImageHeight,
        CardSpacing = CardSpacing,
        CardShadowIntensity = CardShadowIntensity,
        HoverAnimation = HoverAnimation,
        BorderEffect = BorderEffect,
        TitleFontSize = TitleFontSize,
        ShowDescriptionOnCard = ShowDescriptionOnCard,
        PlayButtonStyle = PlayButtonStyle,
        GridColumns = GridColumns,
        SidebarPosition = SidebarPosition,
        SidebarWidth = SidebarWidth,
        NavigationStyle = NavigationStyle,
        TopBarAppearance = TopBarAppearance,
        SearchBarStyle = SearchBarStyle,
        GameGridLayout = GameGridLayout,
        CategoryNavLayout = CategoryNavLayout,
        HeaderSpacing = HeaderSpacing,
        MainContentSpacing = MainContentSpacing,
        ApplicationNameEn = ApplicationNameEn,
        ApplicationNameAr = ApplicationNameAr,
        CyberCafeNameEn = CyberCafeNameEn,
        CyberCafeNameAr = CyberCafeNameAr,
        WelcomeTitleEn = WelcomeTitleEn,
        WelcomeTitleAr = WelcomeTitleAr,
        LoadingScreenTitleEn = LoadingScreenTitleEn,
        LoadingScreenTitleAr = LoadingScreenTitleAr,
        WindowTitleEn = WindowTitleEn,
        WindowTitleAr = WindowTitleAr,
        LogoAssetPath = LogoAssetPath,
        AppIconAssetPath = AppIconAssetPath,
        LoadingLogoAssetPath = LoadingLogoAssetPath,
        LogoSizePx = LogoSizePx,
        LogoPosition = LogoPosition
    };
}

public sealed class ThemeAssetMetadata
{
    public string Id { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string OriginalFileName { get; set; } = string.Empty;
    public string MediaKind { get; set; } = "image";
    public string ContentType { get; set; } = string.Empty;
    public long SizeBytes { get; set; }
    public string RelativeUrl { get; set; } = string.Empty;
    public DateTimeOffset UploadedAtUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class ThemeAuditLog
{
    public long Id { get; set; }
    public DateTimeOffset TimestampUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string ThemeId { get; set; } = string.Empty;
    public string ThemeName { get; set; } = string.Empty;
    public string WorkstationName { get; set; } = string.Empty;
    public string SourceIp { get; set; } = string.Empty;
    public string Details { get; set; } = string.Empty;
}

public sealed class PublishedThemePayload
{
    public long PublishedVersion { get; set; } = 1;
    public DateTimeOffset PublishedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public ThemeConfig Theme { get; set; } = new();
    public List<ThemeAssetMetadata> Assets { get; set; } = new();
}

public sealed class AdminThemesSnapshotResponse
{
    public string ActiveThemeId { get; set; } = string.Empty;
    public long PublishedVersion { get; set; }
    public List<ThemeConfig> Themes { get; set; } = new();
    public List<ThemeAssetMetadata> Assets { get; set; } = new();
    public List<ThemeAuditLog> AuditLogs { get; set; } = new();
}

public sealed class RenameThemeRequest
{
    public string NewName { get; set; } = string.Empty;
}

public sealed class DuplicateThemeRequest
{
    public string NewName { get; set; } = string.Empty;
}

public sealed class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
    public string ConfirmNewPassword { get; set; } = string.Empty;
}

public sealed class ChangePasswordResponse
{
    public bool Success { get; set; }
    public bool RequiresRelogin { get; set; } = true;
    public string Message { get; set; } = string.Empty;
}
