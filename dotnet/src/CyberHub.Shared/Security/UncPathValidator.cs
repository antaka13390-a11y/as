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
            path.StartsWith("covers/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return Uri.TryCreate(path, UriKind.Absolute, out Uri? uri) &&
               (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeFile);
    }
}
