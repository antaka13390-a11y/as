using System.Text.RegularExpressions;
using CyberHub.Shared.Models;

namespace CyberHub.Shared.Security;

public static partial class UncPathValidator
{
    private static readonly HashSet<string> ForbiddenShellChars = new() { "|", "&", ";", "`", "$", ">", "<", "\r", "\n" };

    [GeneratedRegex(@"^\\\\[a-zA-Z0-9._-]+\\[a-zA-Z0-9.$_-]+(?:\\[^<>:""/|?*\r\n]+)*$", RegexOptions.Compiled)]
    private static partial Regex UncRegex();

    [GeneratedRegex(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled)]
    private static partial Regex ExternalIdRegex();

    [GeneratedRegex(@"^[a-z0-9-]+$", RegexOptions.Compiled)]
    private static partial Regex SlugRegex();

    [GeneratedRegex(@"^#(?:[0-9a-fA-F]{6}|[0-9a-fA-F]{8})$", RegexOptions.Compiled)]
    private static partial Regex HexColorRegex();

    public static (bool IsValid, string Error) ValidateGamePath(LaunchType launchType, string executablePath, string externalAppId, string launchArguments)
    {
        if (!string.IsNullOrEmpty(launchArguments))
        {
            foreach (string forbidden in ForbiddenShellChars)
            {
                if (launchArguments.Contains(forbidden, StringComparison.Ordinal))
                {
                    return (false, $"Launch arguments contain disallowed shell character '{forbidden}'.");
                }
            }
        }

        if (launchType == LaunchType.Steam)
        {
            if (string.IsNullOrWhiteSpace(externalAppId) || !uint.TryParse(externalAppId, out _))
            {
                return (false, "Steam launch type requires a numeric Steam AppID.");
            }
        }

        if (launchType == LaunchType.RiotClient)
        {
            if (string.IsNullOrWhiteSpace(externalAppId) || !ExternalIdRegex().IsMatch(externalAppId))
            {
                return (false, "Riot Client launch type requires a valid product ID (e.g., valorant, league_of_legends).");
            }
        }

        if (string.IsNullOrWhiteSpace(executablePath))
        {
            if (launchType == LaunchType.Steam)
            {
                return (true, string.Empty);
            }

            return (false, "Executable path is required.");
        }

        if (executablePath.Contains("..", StringComparison.Ordinal))
        {
            return (false, "Path traversal segments ('..') are not allowed.");
        }

        bool isUnc = executablePath.StartsWith(@"\\", StringComparison.Ordinal);
        if (isUnc && !UncRegex().IsMatch(executablePath))
        {
            return (false, "UNC path format is invalid. Expected \\\\Server\\Share\\Path\\Game.exe.");
        }

        if (!isUnc && !Path.IsPathFullyQualified(executablePath))
        {
            return (false, "Executable path must be a UNC network path (\\\\Server\\Share\\...) or fully qualified client path.");
        }

        string ext = Path.GetExtension(executablePath);
        if (!string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".bat", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(ext, ".cmd", StringComparison.OrdinalIgnoreCase) &&
            launchType == LaunchType.DirectExecutable)
        {
            return (false, "Direct executable launches must target an .exe, .bat, or .cmd binary.");
        }

        return (true, string.Empty);
    }

    public static (bool IsValid, string Error) ValidateCategory(UpsertCategoryRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.NameEn) || req.NameEn.Trim().Length > 64)
        {
            return (false, "Category English name is required and must be 64 characters or fewer.");
        }

        if (string.IsNullOrWhiteSpace(req.NameAr) || req.NameAr.Trim().Length > 64)
        {
            return (false, "Category Arabic name is required and must be 64 characters or fewer.");
        }

        if (!string.IsNullOrWhiteSpace(req.IconKey) && !ExternalIdRegex().IsMatch(req.IconKey.Trim()))
        {
            return (false, "Category icon key contains invalid characters.");
        }

        if (!IsSafeCoverImagePath(req.BannerImagePath))
        {
            return (false, "Category banner path is invalid.");
        }

        return (true, string.Empty);
    }

    public static (bool IsValid, string Error) ValidateThemeConfig(ThemeConfig theme)
    {
        if (string.IsNullOrWhiteSpace(theme.Name) || theme.Name.Trim().Length > 64)
        {
            return (false, "Theme name is required and must be 64 characters or fewer.");
        }

        string[] colors =
        {
            theme.SolidBackgroundColor, theme.GradientStartColor, theme.GradientEndColor,
            theme.PrimaryAccentColor, theme.SecondaryAccentColor, theme.MainBackgroundColor,
            theme.SidebarBackgroundColor, theme.GameCardBackgroundColor, theme.PrimaryTextColor,
            theme.SecondaryTextColor, theme.ButtonBackgroundColor, theme.ButtonTextColor,
            theme.HoverStateColor, theme.BorderColor, theme.ActiveNavItemColor
        };

        foreach (string color in colors)
        {
            if (!HexColorRegex().IsMatch(color ?? string.Empty))
            {
                return (false, $"Invalid HEX color '{color}'. Expected #RRGGBB or #AARRGGBB.");
            }
        }

        if (string.IsNullOrWhiteSpace(theme.ApplicationNameEn) || string.IsNullOrWhiteSpace(theme.ApplicationNameAr))
        {
            return (false, "Both English and Arabic application display names are required.");
        }

        if (!IsSafeCoverImagePath(theme.BackgroundImageAssetPath) ||
            !IsSafeCoverImagePath(theme.BackgroundVideoAssetPath) ||
            !IsSafeCoverImagePath(theme.StaticFallbackImagePath) ||
            !IsSafeCoverImagePath(theme.LogoAssetPath) ||
            !IsSafeCoverImagePath(theme.AppIconAssetPath) ||
            !IsSafeCoverImagePath(theme.LoadingLogoAssetPath))
        {
            return (false, "One or more theme asset paths contain invalid or unsafe characters.");
        }

        return (true, string.Empty);
    }

    public static string GenerateSafeSlug(string input)
    {
        string lower = input.Trim().ToLowerInvariant();
        string dashed = Regex.Replace(lower, @"[^a-z0-9]+", "-").Trim('-');
        if (string.IsNullOrEmpty(dashed) || !SlugRegex().IsMatch(dashed))
        {
            return "cat-" + Guid.NewGuid().ToString("N")[..8];
        }
        return dashed;
    }

    public static bool IsSafeCoverImagePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return true;
        }

        if (path.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        if (path.StartsWith(@"\\", StringComparison.Ordinal))
        {
            return UncRegex().IsMatch(path);
        }

        if (path.StartsWith("/covers/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("covers/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/theme-assets/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("theme-assets/", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/src/assets/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile);
    }

    public static (bool IsValid, string Error) ValidateStorageRootPath(string? rootPath)
    {
        if (string.IsNullOrWhiteSpace(rootPath))
        {
            return (false, "Storage folder path is required.");
        }

        string trimmed = rootPath.Trim();
        if (trimmed.Contains("..", StringComparison.Ordinal))
        {
            return (false, "Path traversal segments ('..') are not allowed.");
        }

        bool isUnc = trimmed.StartsWith(@"\\", StringComparison.Ordinal);
        if (isUnc)
        {
            if (!UncRegex().IsMatch(trimmed))
            {
                return (false, "Invalid UNC path format. Expected \\\\Server\\Share\\Folder.");
            }
            return (true, string.Empty);
        }

        if (!Path.IsPathFullyQualified(trimmed))
        {
            return (false, "Storage folder must be a UNC network path (\\\\Server\\Share\\...) or fully qualified server directory.");
        }

        return (true, string.Empty);
    }

    public static bool IsSafeExternalArtworkUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return false;
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.IsLoopback || uri.HostNameType == UriHostNameType.IPv4 || uri.HostNameType == UriHostNameType.IPv6)
        {
            return false;
        }

        string host = uri.Host.ToLowerInvariant();
        return host == "store.steampowered.com" ||
               host == "shared.fastly.steamstatic.com" ||
               host == "shared.akamai.steamstatic.com" ||
               host == "cdn.cloudflare.steamstatic.com" ||
               host == "steamcdn-a.akamaihd.net" ||
               host == "media.rawg.io" ||
               host == "api.rawg.io";
    }
}
