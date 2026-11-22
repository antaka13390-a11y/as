using System.Diagnostics;

namespace CyberHub.Shared.Services;

public sealed record TrackedProcessHandle(int ProcessId, string ProcessName);

public sealed class ClientLaunchPaths
{
    public string SteamExecutablePath { get; init; } = @"C:\Program Files (x86)\Steam\steam.exe";
    public string RiotClientPath { get; init; } = @"C:\Riot Games\Riot Client\RiotClientServices.exe";
}

public interface IProcessTracker
{
    bool FileExists(string path);
    TrackedProcessHandle? StartProcess(ProcessStartInfo startInfo);
    IReadOnlyList<int> GetChildProcessIds(int parentPid);
    bool IsProcessRunning(int pid);
    IReadOnlyList<int> GetProcessesByName(string processName);
}
