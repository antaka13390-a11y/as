using System.Diagnostics;
using System.IO;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;

namespace CyberHub.ClientLauncher.Services;

public sealed record LaunchOutcome(bool Success, string Message);

public sealed class GameLaunchService
{
    private readonly ClientConfig _config;
    private readonly SemaphoreSlim _launchGate = new(1, 1);
    private bool _isGameRunning;

    public event EventHandler<bool>? GameRunningStateChanged;

    public GameLaunchService(ClientConfig config)
    {
        _config = config;
    }

    public bool IsGameRunning => _isGameRunning;

    public async Task<LaunchOutcome> LaunchGameAsync(
        GameEntry game,
        Action onMinimizeLauncher,
        Action onRestoreLauncher,
        CancellationToken ct = default)
    {
        if (!await _launchGate.WaitAsync(0, ct))
        {
            return new LaunchOutcome(false, "A game launch is already in progress.");
        }

        if (_isGameRunning)
        {
            _launchGate.Release();
            return new LaunchOutcome(false, $"{game.Name} or another title is already running on this station.");
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
                return new LaunchOutcome(false, $"Invalid game configuration: {validationError}");
            }

            ProcessStartInfo? startInfo = BuildStartInfo(game, out string? preflightError);
            if (startInfo is null)
            {
                return new LaunchOutcome(false, preflightError ?? "Failed to resolve game executable.");
            }

            _isGameRunning = true;
            GameRunningStateChanged?.Invoke(this, true);
            onMinimizeLauncher();

            using Process? launched = Process.Start(startInfo);
            if (launched is null)
            {
                _isGameRunning = false;
                GameRunningStateChanged?.Invoke(this, false);
                onRestoreLauncher();
                return new LaunchOutcome(false, "Windows failed to start the game process.");
            }

            await MonitorGameExitAsync(launched, game.ProcessNameToMonitor, ct);

            return new LaunchOutcome(true, $"{game.Name} session completed.");
        }
        catch (Exception ex)
        {
            return new LaunchOutcome(false, $"Launch failed: {ex.Message}");
        }
        finally
        {
            _isGameRunning = false;
            GameRunningStateChanged?.Invoke(this, false);
            onRestoreLauncher();
            _launchGate.Release();
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
                if (!File.Exists(game.ExecutablePath))
                {
                    error = $"Executable not reachable on network/disk: {game.ExecutablePath}";
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
                if (File.Exists(_config.SteamExecutablePath))
                {
                    string args = $"-applaunch {game.ExternalAppId} {game.LaunchArguments}".Trim();
                    return new ProcessStartInfo
                    {
                        FileName = _config.SteamExecutablePath,
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
                string riotPath = !string.IsNullOrWhiteSpace(game.ExecutablePath) && File.Exists(game.ExecutablePath)
                    ? game.ExecutablePath
                    : _config.RiotClientPath;

                if (!File.Exists(riotPath))
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

    private static async Task MonitorGameExitAsync(Process launchedProcess, string processNameToMonitor, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(processNameToMonitor))
        {
            await launchedProcess.WaitForExitAsync(ct);
            return;
        }

        string cleanName = Path.GetFileNameWithoutExtension(processNameToMonitor);

        // Wait up to 45s for launcher handoff (Steam/Riot) to spawn the actual game binary
        Process? target = null;
        for (int i = 0; i < 45 && !ct.IsCancellationRequested; i++)
        {
            Process[] matches = Process.GetProcessesByName(cleanName);
            if (matches.Length > 0)
            {
                target = matches[0];
                foreach (Process extra in matches.Skip(1))
                {
                    extra.Dispose();
                }
                break;
            }

            if (launchedProcess.HasExited && i > 10)
            {
                break;
            }

            await Task.Delay(1000, ct);
        }

        if (target is not null)
        {
            using (target)
            {
                await target.WaitForExitAsync(ct);
            }
        }
        else if (!launchedProcess.HasExited)
        {
            await launchedProcess.WaitForExitAsync(ct);
        }
    }
}
