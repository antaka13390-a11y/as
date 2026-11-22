using System.Text.RegularExpressions;
using CyberHub.Shared.Models;

namespace CyberHub.Shared.Services;

public sealed record SteamManifestInfo(string AppId, string Name, string InstallDir);

public static partial class GameDetectionHeuristics
{
    private static readonly string[] IgnoredPathSegments =
    {
        "\\windows\\",
        "\\system32\\",
        "\\syswow64\\",
        "\\$recycle.bin\\",
        "\\system volume information\\",
        "\\programdata\\package cache\\",
        "\\appdata\\local\\temp\\",
        "\\temp\\",
        "\\tmp\\",
        "\\_commonredist\\",
        "\\directx\\",
        "\\vcredist\\",
        "\\dotnet\\",
        "\\prerequisites\\",
        "\\support\\",
        "\\__installer\\",
        "\\easyanticheat\\",
        "\\battleye\\"
    };

    private static readonly string[] IgnoredExecutablePrefixesOrTokens =
    {
        "unins",
        "uninstall",
        "setup",
        "install",
        "vcredist",
        "dxsetup",
        "dotnetfx",
        "ndp4",
        "crashreport",
        "crashhandler",
        "ue4prereq",
        "ueprereq",
        "updater",
        "patcher",
        "redist",
        "physx",
        "cefprocess",
        "webhelper",
        "steamwebhelper",
        "riotclientcrashhandler",
        "vanguard",
        "vgtray",
        "bootstrapper",
        "7z",
        "cmd",
        "powershell"
    };

    [GeneratedRegex(@"\b(x64|x86|win64|win32|shipping|retail|final|demo|tm|ltd)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex NoiseTokenRegex();

    [GeneratedRegex(@"[^a-z0-9]+", RegexOptions.Compiled)]
    private static partial Regex NonAlnumRegex();

    [GeneratedRegex(@"""appid""\s+""(\d+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SteamAppIdRegex();

    [GeneratedRegex(@"""name""\s+""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SteamNameRegex();

    [GeneratedRegex(@"""installdir""\s+""([^""]+)""", RegexOptions.IgnoreCase | RegexOptions.Compiled)]
    private static partial Regex SteamInstallDirRegex();

    public static bool ShouldIgnorePathOrFile(string fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath))
        {
            return true;
        }

        string normalized = fullPath.Replace('/', '\\').ToLowerInvariant();
        foreach (string seg in IgnoredPathSegments)
        {
            if (normalized.Contains(seg, StringComparison.Ordinal))
            {
                return true;
            }
        }

        string ext = Path.GetExtension(normalized);
        if (!string.Equals(ext, ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        string stem = Path.GetFileNameWithoutExtension(normalized);
        if (string.IsNullOrWhiteSpace(stem) || stem.Length < 2)
        {
            return true;
        }

        foreach (string token in IgnoredExecutablePrefixesOrTokens)
        {
            if (stem.Equals(token, StringComparison.Ordinal) ||
                stem.StartsWith(token, StringComparison.Ordinal) ||
                stem.Contains(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public static string NormalizeGameName(string rawName)
    {
        if (string.IsNullOrWhiteSpace(rawName))
        {
            return string.Empty;
        }

        string cleaned = rawName
            .Replace("™", string.Empty, StringComparison.Ordinal)
            .Replace("®", string.Empty, StringComparison.Ordinal)
            .Replace("©", string.Empty, StringComparison.Ordinal);

        cleaned = NoiseTokenRegex().Replace(cleaned, " ");
        cleaned = NonAlnumRegex().Replace(cleaned.ToLowerInvariant(), string.Empty);
        return cleaned;
    }

    public static string DeriveReadableNameFromFileOrFolder(string executablePath, string? productName = null, string? fileDescription = null)
    {
        if (!string.IsNullOrWhiteSpace(productName) && !LooksGenericToolName(productName))
        {
            return productName.Trim();
        }

        if (!string.IsNullOrWhiteSpace(fileDescription) && !LooksGenericToolName(fileDescription))
        {
            return fileDescription.Trim();
        }

        string? parentDir = Path.GetFileName(Path.GetDirectoryName(executablePath.TrimEnd('\\', '/')));
        string stem = Path.GetFileNameWithoutExtension(executablePath);

        string raw = (!string.IsNullOrWhiteSpace(parentDir) &&
                      !parentDir.Equals("bin", StringComparison.OrdinalIgnoreCase) &&
                      !parentDir.Equals("win64", StringComparison.OrdinalIgnoreCase) &&
                      !parentDir.Equals("binaries", StringComparison.OrdinalIgnoreCase) &&
                      !parentDir.Equals("common", StringComparison.OrdinalIgnoreCase))
            ? parentDir
            : stem;

        string spaced = Regex.Replace(raw, @"[_\-.]+", " ");
        spaced = Regex.Replace(spaced, @"(?<=[a-z])(?=[A-Z])", " ");
        spaced = NoiseTokenRegex().Replace(spaced, " ");
        return Regex.Replace(spaced, @"\s{2,}", " ").Trim();
    }

    private static bool LooksGenericToolName(string text)
    {
        string lower = text.Trim().ToLowerInvariant();
        return lower.Contains("unreal engine", StringComparison.Ordinal) ||
               lower.Contains("unity player", StringComparison.Ordinal) ||
               lower.Contains("bootstrap", StringComparison.Ordinal) ||
               lower.Contains("installer", StringComparison.Ordinal) ||
               lower.Contains("microsoft corporation", StringComparison.Ordinal) ||
               lower.Length < 2;
    }

    public static SteamManifestInfo? ParseSteamAppManifest(string acfContent)
    {
        if (string.IsNullOrWhiteSpace(acfContent))
        {
            return null;
        }

        Match appMatch = SteamAppIdRegex().Match(acfContent);
        Match nameMatch = SteamNameRegex().Match(acfContent);
        Match dirMatch = SteamInstallDirRegex().Match(acfContent);

        if (!appMatch.Success || !nameMatch.Success)
        {
            return null;
        }

        return new SteamManifestInfo(
            appMatch.Groups[1].Value.Trim(),
            nameMatch.Groups[1].Value.Trim(),
            dirMatch.Success ? dirMatch.Groups[1].Value.Trim() : string.Empty);
    }

    public static (bool IsRiot, string ProductId, string DisplayName) DetectRiotByFolderOrBinary(string path)
    {
        string lower = path.Replace('/', '\\').ToLowerInvariant();
        if (lower.Contains("valorant", StringComparison.Ordinal))
        {
            return (true, "valorant", "VALORANT");
        }
        if (lower.Contains("league of legends", StringComparison.Ordinal) || lower.Contains("leagueclient", StringComparison.Ordinal))
        {
            return (true, "league_of_legends", "League of Legends");
        }
        if (lower.Contains("teamfight tactics", StringComparison.Ordinal))
        {
            return (true, "teamfight_tactics", "Teamfight Tactics");
        }
        if (lower.Contains("2xko", StringComparison.Ordinal))
        {
            return (true, "2xko", "2XKO");
        }
        return (false, string.Empty, string.Empty);
    }

    public static (bool IsDuplicate, string Reason, Guid? ExistingId, string ExistingName) CheckDuplicate(
        DetectedGameCandidate candidate,
        IReadOnlyList<GameEntry> existingGames)
    {
        string normExe = candidate.ExecutablePath.Trim().Replace('/', '\\');
        string normName = !string.IsNullOrWhiteSpace(candidate.NormalizedName)
            ? candidate.NormalizedName
            : NormalizeGameName(candidate.DetectedName);

        foreach (GameEntry game in existingGames)
        {
            if (!string.IsNullOrWhiteSpace(normExe) &&
                !string.IsNullOrWhiteSpace(game.ExecutablePath) &&
                string.Equals(game.ExecutablePath.Trim().Replace('/', '\\'), normExe, StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"Matches existing executable path of '{game.Name}'.", game.Id, game.Name);
            }

            if (!string.IsNullOrWhiteSpace(candidate.ExternalAppId) &&
                !string.IsNullOrWhiteSpace(game.ExternalAppId) &&
                candidate.LaunchType == game.LaunchType &&
                string.Equals(game.ExternalAppId.Trim(), candidate.ExternalAppId.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return (true, $"Matches existing AppID ({candidate.ExternalAppId}) of '{game.Name}'.", game.Id, game.Name);
            }

            string existingNorm = NormalizeGameName(game.Name);
            if (!string.IsNullOrWhiteSpace(normName) &&
                normName.Length >= 3 &&
                string.Equals(existingNorm, normName, StringComparison.Ordinal))
            {
                return (true, $"Normalized game title matches existing library game '{game.Name}'.", game.Id, game.Name);
            }
        }

        return (false, string.Empty, null, string.Empty);
    }
}
