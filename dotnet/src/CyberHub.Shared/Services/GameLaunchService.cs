using System.Diagnostics;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;

namespace CyberHub.Shared.Services;

public sealed record LaunchOutcome(bool Success, bool RequiresAdminRecovery, string MessageKey, string MessageDetail);

public sealed class GameLaunchService
{
    private readonly ClientLaunchPaths _paths;
    private readonly LocalGameSessionManager _sessionManager;
    private readonly IProcessTracker _tracker;
    private readonly TimeSpan _pollInterval;
    private readonly TimeSpan _launcherHandoffWindow;

    public GameLaunchService(
        ClientLaunchPaths paths,
        LocalGameSessionManager sessionManager,
        IProcessTracker tracker,
        TimeSpan? pollInterval = null,
        TimeSpan? launcherHandoffWindow = null)
    {
        _paths = paths;
        _sessionManager = sessionManager;
        _tracker = tracker;
        _pollInterval = pollInterval ?? TimeSpan.FromMilliseconds(1000);
        _launcherHandoffWindow = launcherHandoffWindow ?? TimeSpan.FromSeconds(60);
    }

    public LocalGameSessionManager SessionManager => _sessionManager;

    public async Task<LaunchOutcome> LaunchGameAsync(
        GameEntry game,
        Action onMinimizeLauncher,
        Action onRestoreLauncher,
        CancellationToken ct = default)
    {
        List<string> monitoredNames = BuildMonitoredProcessNames(game);

        (bool acquired, LocalGameSessionState? existingSession, string? _) =
            await _sessionManager.TryAcquireAsync(game, monitoredNames, ct);

        if (!acquired)
        {
            return new LaunchOutcome(
                false,
                existingSession?.RequiresAdminRecovery ?? false,
                "Session.DuplicateBlocked",
                existingSession?.GameName ?? game.Name);
        }

        try
        {
            (bool isValid, string validationError) = UncPathValidator.ValidateGamePath(
                game.LaunchType,
                game.ExecutablePath,
                game.ExternalAppId,
                game.LaunchArguments);

            if (!isValid)
            {
                await _sessionManager.ReleaseAfterVerifiedExitAsync(ct);
                return new LaunchOutcome(false, false, "Launch.InvalidConfig", validationError);
            }

            ProcessStartInfo? startInfo = BuildStartInfo(game, out string? preflightError);
            if (startInfo is null)
            {
                await _sessionManager.ReleaseAfterVerifiedExitAsync(ct);
                return new LaunchOutcome(false, false, "Launch.ExecutableMissing", preflightError ?? "Executable missing.");
            }

            onMinimizeLauncher();

            TrackedProcessHandle? rootProc = _tracker.StartProcess(startInfo);
            bool isShellUriHandoff = rootProc is null && startInfo.UseShellExecute && game.LaunchType == LaunchType.Steam && monitoredNames.Count > 0;
            if (rootProc is null && !isShellUriHandoff)
            {
                onRestoreLauncher();
                await _sessionManager.ReleaseAfterVerifiedExitAsync(ct);
                return new LaunchOutcome(false, false, "Launch.StartFailed", "Failed to start process.");
            }

            if (rootProc is not null)
            {
                await _sessionManager.RegisterTrackedPidsAsync(rootProc.ProcessId, Array.Empty<int>(), ct);
            }

            bool verifiedCleanExit = await MonitorGameTreeUntilExitAsync(
                rootProc?.ProcessId,
                game.LaunchType,
                monitoredNames,
                ct);

            if (!verifiedCleanExit)
            {
                await _sessionManager.MarkTrackingFailureAndKeepLockedAsync(
                    "Unable to verify that the game or launcher child process exited cleanly.",
                    ct);
                onRestoreLauncher();
                return new LaunchOutcome(false, true, "Session.RecoveryRequired", game.Name);
            }

            await _sessionManager.ReleaseAfterVerifiedExitAsync(ct);
            onRestoreLauncher();
            return new LaunchOutcome(true, false, "Session.Completed", game.Name);
        }
        catch (OperationCanceledException)
        {
            await _sessionManager.MarkTrackingFailureAndKeepLockedAsync("Session monitoring was interrupted.", CancellationToken.None);
            onRestoreLauncher();
            return new LaunchOutcome(false, true, "Session.RecoveryRequired", game.Name);
        }
        catch (Exception ex)
        {
            await _sessionManager.MarkTrackingFailureAndKeepLockedAsync(ex.Message, CancellationToken.None);
            onRestoreLauncher();
            return new LaunchOutcome(false, true, "Session.RecoveryRequired", ex.Message);
        }
    }

    private ProcessStartInfo? BuildStartInfo(GameEntry game, out string? error)
    {
        error = null;

        switch (game.LaunchType)
        {
            case LaunchType.DirectExecutable:
            case LaunchType.ExternalLauncher:
            {
                if (!_tracker.FileExists(game.ExecutablePath))
                {
                    error = $"Executable not reachable: {game.ExecutablePath}";
                    return null;
                }

                string? workingDir = Path.GetDirectoryName(game.ExecutablePath);
                return new ProcessStartInfo
                {
                    FileName = game.ExecutablePath,
                    Arguments = game.LaunchArguments,
                    WorkingDirectory = string.IsNullOrWhiteSpace(workingDir) ? AppContext.BaseDirectory : workingDir,
                    UseShellExecute = false
                };
            }

            case LaunchType.Steam:
            {
                if (_tracker.FileExists(_paths.SteamExecutablePath))
                {
                    string args = $"-applaunch {game.ExternalAppId} {game.LaunchArguments}".Trim();
                    return new ProcessStartInfo
                    {
                        FileName = _paths.SteamExecutablePath,
                        Arguments = args,
                        UseShellExecute = false
                    };
                }

                return new ProcessStartInfo
                {
                    FileName = $"steam://rungameid/{game.ExternalAppId}",
                    UseShellExecute = true
                };
            }

            case LaunchType.RiotClient:
            {
                string riotPath = !string.IsNullOrWhiteSpace(game.ExecutablePath) && _tracker.FileExists(game.ExecutablePath)
                    ? game.ExecutablePath
                    : _paths.RiotClientPath;

                if (!_tracker.FileExists(riotPath))
                {
                    error = $"Riot Client executable not found at {riotPath}.";
                    return null;
                }

                string args = $"--launch-product={game.ExternalAppId} --launch-patchline=live {game.LaunchArguments}".Trim();
                return new ProcessStartInfo
                {
                    FileName = riotPath,
                    Arguments = args,
                    UseShellExecute = false
                };
            }

            default:
                error = "Unsupported launch type.";
                return null;
        }
    }

    private async Task<bool> MonitorGameTreeUntilExitAsync(
        int? rootPid,
        LaunchType launchType,
        IReadOnlyList<string> monitoredNames,
        CancellationToken ct)
    {
        var trackedPids = new HashSet<int>();
        if (rootPid.HasValue)
        {
            trackedPids.Add(rootPid.Value);
        }

        DateTimeOffset handoffDeadline = DateTimeOffset.UtcNow.Add(_launcherHandoffWindow);
        bool sawExternalChildOrNamedProcess = launchType == LaunchType.DirectExecutable;

        while (!ct.IsCancellationRequested)
        {
            foreach (int pid in trackedPids.ToList())
            {
                IReadOnlyList<int> children = _tracker.GetChildProcessIds(pid);
                if (children.Count > 0)
                {
                    sawExternalChildOrNamedProcess = true;
                    foreach (int childPid in children)
                    {
                        trackedPids.Add(childPid);
                    }
                }
            }

            foreach (string procName in monitoredNames)
            {
                IReadOnlyList<int> namedPids = _tracker.GetProcessesByName(procName);
                if (namedPids.Count > 0)
                {
                    sawExternalChildOrNamedProcess = true;
                    foreach (int pid in namedPids)
                    {
                        trackedPids.Add(pid);
                    }
                }
            }

            if (trackedPids.Count > 0)
            {
                await _sessionManager.RegisterTrackedPidsAsync(rootPid ?? trackedPids.First(), trackedPids, ct);
            }

            bool anyAlive = trackedPids.Any(_tracker.IsProcessRunning);
            if (!anyAlive)
            {
                if (launchType != LaunchType.DirectExecutable &&
                    !sawExternalChildOrNamedProcess &&
                    DateTimeOffset.UtcNow < handoffDeadline)
                {
                    await Task.Delay(_pollInterval, ct);
                    continue;
                }

                // External launchers (Steam, Riot, etc.) must hand off to a child or monitored game process;
                // if none is ever observed before the handoff window expires, fail closed to Admin Recovery.
                if (launchType != LaunchType.DirectExecutable && !sawExternalChildOrNamedProcess)
                {
                    return false;
                }

                return true;
            }

            await Task.Delay(_pollInterval, ct);
        }

        return false;
    }

    private static List<string> BuildMonitoredProcessNames(GameEntry game)
    {
        var names = new List<string>();
        if (!string.IsNullOrWhiteSpace(game.ProcessNameToMonitor))
        {
            foreach (string part in game.ProcessNameToMonitor.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string stem = Path.GetFileNameWithoutExtension(part);
                if (!string.IsNullOrWhiteSpace(stem) && !names.Contains(stem, StringComparer.OrdinalIgnoreCase))
                {
                    names.Add(stem);
                }
            }
        }

        if (!string.IsNullOrWhiteSpace(game.ExecutablePath))
        {
            string exeName = Path.GetFileNameWithoutExtension(game.ExecutablePath);
            bool isLauncherStub = string.Equals(exeName, "steam", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(exeName, "riotclientservices", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(exeName) &&
                (game.LaunchType == LaunchType.DirectExecutable || !isLauncherStub) &&
                !names.Contains(exeName, StringComparer.OrdinalIgnoreCase))
            {
                names.Add(exeName);
            }
        }

        return names;
    }
}
