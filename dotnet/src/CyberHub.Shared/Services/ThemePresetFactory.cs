using CyberHub.Shared.Models;

namespace CyberHub.Shared.Services;

public static class ThemePresetFactory
{
    public static List<ThemeConfig> CreateBuiltInPresets()
    {
        return new List<ThemeConfig>
        {
            CreateNeonPurple(isDefault: true, isPublished: true),
            CreateCyberBlue(),
            CreateDarkRed(),
            CreateLuxuryGold(),
            CreateMinimalDark(),
            CreateCustomTemplate()
        };
    }

    public static ThemeConfig CreatePresetByKey(string presetKey) => presetKey switch
    {
        "CyberBlue" => CreateCyberBlue(),
        "DarkRed" => CreateDarkRed(),
        "LuxuryGold" => CreateLuxuryGold(),
        "MinimalDark" => CreateMinimalDark(),
        "Custom" => CreateCustomTemplate(),
        _ => CreateNeonPurple(isDefault: true, isPublished: false)
    };

    public static ThemeConfig CreateNeonPurple(bool isDefault = false, bool isPublished = false) => new()
    {
        Id = "theme-neon-purple",
        Name = "Neon Purple",
        PresetKey = "NeonPurple",
        IsDefault = isDefault,
        IsPublished = isPublished,
        Version = 1,
        BackgroundType = ThemeBackgroundType.Gradient,
        SolidBackgroundColor = "#08090D",
        GradientStartColor = "#08090D",
        GradientEndColor = "#19102E",
        GradientAngleDegrees = 135,
        OverlayOpacity = 0.5,
        PrimaryAccentColor = "#8B5CF6",
        SecondaryAccentColor = "#06B6D4",
        MainBackgroundColor = "#08090D",
        SidebarBackgroundColor = "#0F1118",
        GameCardBackgroundColor = "#11131C",
        PrimaryTextColor = "#F8FAFC",
        SecondaryTextColor = "#94A3B8",
        ButtonBackgroundColor = "#8B5CF6",
        ButtonTextColor = "#FFFFFF",
        HoverStateColor = "#1B1E2E",
        BorderColor = "#252A3D",
        ActiveNavItemColor = "#2B1E54",
        CardLayoutStyle = CardLayoutStyle.Standard,
        CardShape = CardShapeStyle.Rounded,
        CardBorderRadius = 12,
        CardWidth = 248,
        CardHeight = 368,
        CoverImageHeight = 276,
        CardSpacing = 20,
        BorderEffect = CardBorderEffect.Subtle,
        PlayButtonStyle = PlayButtonStyle.Gradient,
        GridColumns = 4
    };

    public static ThemeConfig CreateCyberBlue() => new()
    {
        Id = "theme-cyber-blue",
        Name = "Cyber Blue",
        PresetKey = "CyberBlue",
        Version = 1,
        BackgroundType = ThemeBackgroundType.Gradient,
        SolidBackgroundColor = "#050A12",
        GradientStartColor = "#050A12",
        GradientEndColor = "#0B2239",
        GradientAngleDegrees = 145,
        PrimaryAccentColor = "#0EA5E9",
        SecondaryAccentColor = "#22D3EE",
        MainBackgroundColor = "#050A12",
        SidebarBackgroundColor = "#091320",
        GameCardBackgroundColor = "#0D1A2B",
        PrimaryTextColor = "#F0F9FF",
        SecondaryTextColor = "#93C5FD",
        ButtonBackgroundColor = "#0284C7",
        ButtonTextColor = "#FFFFFF",
        HoverStateColor = "#132942",
        BorderColor = "#1D3B5E",
        ActiveNavItemColor = "#0E3459",
        CardLayoutStyle = CardLayoutStyle.LargeArtwork,
        CardShape = CardShapeStyle.CutCorner,
        CardBorderRadius = 10,
        CardWidth = 272,
        CardHeight = 396,
        CoverImageHeight = 310,
        CardSpacing = 22,
        BorderEffect = CardBorderEffect.NeonGlow,
        PlayButtonStyle = PlayButtonStyle.Gradient,
        GridColumns = 4
    };

    public static ThemeConfig CreateDarkRed() => new()
    {
        Id = "theme-dark-red",
        Name = "Dark Red",
        PresetKey = "DarkRed",
        Version = 1,
        BackgroundType = ThemeBackgroundType.Gradient,
        SolidBackgroundColor = "#0B0709",
        GradientStartColor = "#0B0709",
        GradientEndColor = "#290B12",
        GradientAngleDegrees = 130,
        PrimaryAccentColor = "#EF4444",
        SecondaryAccentColor = "#F97316",
        MainBackgroundColor = "#0B0709",
        SidebarBackgroundColor = "#130B0E",
        GameCardBackgroundColor = "#180E12",
        PrimaryTextColor = "#FEF2F2",
        SecondaryTextColor = "#FCA5A5",
        ButtonBackgroundColor = "#DC2626",
        ButtonTextColor = "#FFFFFF",
        HoverStateColor = "#2A151B",
        BorderColor = "#3B1B22",
        ActiveNavItemColor = "#4A161F",
        CardLayoutStyle = CardLayoutStyle.Standard,
        CardShape = CardShapeStyle.Sharp,
        CardBorderRadius = 4,
        CardWidth = 244,
        CardHeight = 360,
        CoverImageHeight = 272,
        CardSpacing = 18,
        BorderEffect = CardBorderEffect.NeonGlow,
        PlayButtonStyle = PlayButtonStyle.Solid,
        GridColumns = 4
    };

    public static ThemeConfig CreateLuxuryGold() => new()
    {
        Id = "theme-luxury-gold",
        Name = "Luxury Gold",
        PresetKey = "LuxuryGold",
        Version = 1,
        BackgroundType = ThemeBackgroundType.Gradient,
        SolidBackgroundColor = "#0A0908",
        GradientStartColor = "#0A0908",
        GradientEndColor = "#211A0E",
        GradientAngleDegrees = 140,
        PrimaryAccentColor = "#D4AF37",
        SecondaryAccentColor = "#FDE68A",
        MainBackgroundColor = "#0A0908",
        SidebarBackgroundColor = "#12100C",
        GameCardBackgroundColor = "#171410",
        PrimaryTextColor = "#FAF7F0",
        SecondaryTextColor = "#C9B99A",
        ButtonBackgroundColor = "#B8860B",
        ButtonTextColor = "#0A0908",
        HoverStateColor = "#241F17",
        BorderColor = "#3D3220",
        ActiveNavItemColor = "#382C15",
        CardLayoutStyle = CardLayoutStyle.LargeArtwork,
        CardShape = CardShapeStyle.Rounded,
        CardBorderRadius = 14,
        CardWidth = 264,
        CardHeight = 384,
        CoverImageHeight = 296,
        CardSpacing = 22,
        BorderEffect = CardBorderEffect.Subtle,
        PlayButtonStyle = PlayButtonStyle.Solid,
        GridColumns = 4
    };

    public static ThemeConfig CreateMinimalDark() => new()
    {
        Id = "theme-minimal-dark",
        Name = "Minimal Dark",
        PresetKey = "MinimalDark",
        Version = 1,
        BackgroundType = ThemeBackgroundType.Solid,
        SolidBackgroundColor = "#0A0A0C",
        GradientStartColor = "#0A0A0C",
        GradientEndColor = "#0A0A0C",
        PrimaryAccentColor = "#E2E8F0",
        SecondaryAccentColor = "#94A3B8",
        MainBackgroundColor = "#0A0A0C",
        SidebarBackgroundColor = "#111115",
        GameCardBackgroundColor = "#141419",
        PrimaryTextColor = "#F8FAFC",
        SecondaryTextColor = "#94A3B8",
        ButtonBackgroundColor = "#E2E8F0",
        ButtonTextColor = "#0A0A0C",
        HoverStateColor = "#1D1D24",
        BorderColor = "#24242E",
        ActiveNavItemColor = "#20202A",
        CardLayoutStyle = CardLayoutStyle.Compact,
        CardShape = CardShapeStyle.Rounded,
        CardBorderRadius = 8,
        CardWidth = 216,
        CardHeight = 310,
        CoverImageHeight = 224,
        CardSpacing = 14,
        BorderEffect = CardBorderEffect.Subtle,
        PlayButtonStyle = PlayButtonStyle.Outline,
        GridColumns = 5
    };

    public static ThemeConfig CreateCustomTemplate()
    {
        ThemeConfig custom = CreateNeonPurple(isDefault: false, isPublished: false);
        custom.Id = "theme-custom-starter";
        custom.Name = "Custom Theme";
        custom.PresetKey = "Custom";
        return custom;
    }
}
