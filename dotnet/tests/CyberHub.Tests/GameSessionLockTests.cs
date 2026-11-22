using System.Collections.Concurrent;
using System.Diagnostics;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using CyberHub.Shared.Services;
using Xunit;

namespace CyberHub.Tests;

internal sealed class FakeProcessTracker : IProcessTracker
{
    private int _nextPid = 1000;
    public ConcurrentDictionary<int, bool> RunningPids { get; } = new();
    public ConcurrentDictionary<int, List<int>> ChildPids { get; } = new();
    public ConcurrentDictionary<string, List<int>> NamedPids { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool ThrowOnProcessCheck { get; set; }

    public bool FileExists(string path) => true;

    public TrackedProcessHandle? StartProcess(ProcessStartInfo startInfo)
    {
        int pid = Interlocked.Increment(ref _nextPid);
        RunningPids[pid] = true;
        return new TrackedProcessHandle(pid, Path.GetFileNameWithoutExtension(startInfo.FileName));
    }

    public IReadOnlyList<int> GetChildProcessIds(int parentPid)
    {
        return ChildPids.TryGetValue(parentPid, out List<int>? kids) ? kids : Array.Empty<int>();
    }

    public bool IsProcessRunning(int pid)
    {
        if (ThrowOnProcessCheck)
        {
            throw new InvalidOperationException("Simulated OS process query failure.");
        }
        return RunningPids.TryGetValue(pid, out bool alive) && alive;
    }

    public IReadOnlyList<int> GetProcessesByName(string processName)
    {
        return NamedPids.TryGetValue(processName, out List<int>? pids)
            ? pids.Where(IsProcessRunning).ToList()
            : Array.Empty<int>();
    }
}

public sealed class GameSessionLockTests : IDisposable
{
    private readonly string _tempRoot;

    public GameSessionLockTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "cyberhub-lock-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    [Fact]
    public async Task LaunchGame_PreventsSecondGameLaunchWhileFirstIsRunning_AndReleasesAfterExit()
    {
        var tracker = new FakeProcessTracker();
        var sessionMgr = new LocalGameSessionManager(tracker, Path.Combine(_tempRoot, "pc-01"));
        var launchService = new GameLaunchService(
            new ClientLaunchPaths(),
            sessionMgr,
            tracker,
            pollInterval: TimeSpan.FromMilliseconds(20),
            launcherHandoffWindow: TimeSpan.FromMilliseconds(100));

        var game1 = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "Game One",
            LaunchType = LaunchType.DirectExecutable,
            ExecutablePath = @"\\CCBOOT-SRV\Games\GameOne\game1.exe"
        };
        var game2 = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "Game Two",
            LaunchType = LaunchType.DirectExecutable,
            ExecutablePath = @"\\CCBOOT-SRV\Games\GameTwo\game2.exe"
        };

        bool minimized = false;
        bool restored = false;

        Task<LaunchOutcome> firstLaunchTask = launchService.LaunchGameAsync(
            game1,
            () => minimized = true,
            () => restored = true);

        // Wait until first process starts
        await Task.Delay(50);
        Assert.True(sessionMgr.IsLocked);
        Assert.True(minimized);

        // Attempt a second concurrent launch -> must be blocked inside GameLaunchService
        LaunchOutcome secondOutcome = await launchService.LaunchGameAsync(game2, () => { }, () => { });
        Assert.False(secondOutcome.Success);
        Assert.Equal("Session.DuplicateBlocked", secondOutcome.MessageKey);

        // Terminate first game process cleanly
        foreach (int pid in tracker.RunningPids.Keys)
        {
            tracker.RunningPids[pid] = false;
        }

        LaunchOutcome firstOutcome = await firstLaunchTask;
        Assert.True(firstOutcome.Success);
        Assert.True(restored);
        Assert.False(sessionMgr.IsLocked);
    }

    [Fact]
    public async Task ExternalLauncher_KeepsLockWhenBootstrapperExitsButChildProcessRuns()
    {
        var tracker = new FakeProcessTracker();
        var sessionMgr = new LocalGameSessionManager(tracker, Path.Combine(_tempRoot, "pc-02"));
        var launchService = new GameLaunchService(
            new ClientLaunchPaths(),
            sessionMgr,
            tracker,
            pollInterval: TimeSpan.FromMilliseconds(20),
            launcherHandoffWindow: TimeSpan.FromMilliseconds(300));

        var riotGame = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "Valor Protocol",
            LaunchType = LaunchType.RiotClient,
            ExternalAppId = "valorant",
            ExecutablePath = @"\\CCBOOT-SRV\Games\Riot Games\RiotClientServices.exe",
            ProcessNameToMonitor = "VALORANT-Win64-Shipping"
        };

        bool restored = false;
        Task<LaunchOutcome> launchTask = launchService.LaunchGameAsync(riotGame, () => { }, () => restored = true);

        await Task.Delay(40);
        int bootstrapperPid = tracker.RunningPids.Keys.Single();

        // Spawn child game process and exit initial bootstrapper
        int childGamePid = 2500;
        tracker.RunningPids[childGamePid] = true;
        tracker.ChildPids[bootstrapperPid] = new List<int> { childGamePid };
        tracker.NamedPids["VALORANT-Win64-Shipping"] = new List<int> { childGamePid };

        await Task.Delay(50);
        tracker.RunningPids[bootstrapperPid] = false;

        // Wait longer than poll interval: session MUST STILL BE LOCKED because child process is running
        await Task.Delay(120);
        Assert.True(sessionMgr.IsLocked);
        Assert.False(restored);

        // Now exit the actual child game process
        tracker.RunningPids[childGamePid] = false;
        LaunchOutcome outcome = await launchTask;
        Assert.True(outcome.Success);
        Assert.True(restored);
        Assert.False(sessionMgr.IsLocked);
    }

    [Fact]
    public async Task TrackingFailure_FailsSafelyAndRequiresAdminRecoveryCode()
    {
        var tracker = new FakeProcessTracker();
        var sessionMgr = new LocalGameSessionManager(tracker, Path.Combine(_tempRoot, "pc-03"));
        var launchService = new GameLaunchService(
            new ClientLaunchPaths(),
            sessionMgr,
            tracker,
            pollInterval: TimeSpan.FromMilliseconds(20),
            launcherHandoffWindow: TimeSpan.FromMilliseconds(100));

        var game = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "Crash Prone Game",
            LaunchType = LaunchType.DirectExecutable,
            ExecutablePath = @"\\CCBOOT-SRV\Games\Crash\game.exe"
        };

        Task<LaunchOutcome> launchTask = launchService.LaunchGameAsync(game, () => { }, () => { });
        await Task.Delay(40);

        // Simulate tracking failure mid-session
        tracker.ThrowOnProcessCheck = true;

        LaunchOutcome outcome = await launchTask;
        Assert.False(outcome.Success);
        Assert.True(outcome.RequiresAdminRecovery);
        Assert.True(sessionMgr.IsLocked);

        // Normal customer cannot bypass or start another game
        LaunchOutcome blocked = await launchService.LaunchGameAsync(game, () => { }, () => { });
        Assert.False(blocked.Success);

        // Admin issues station-scoped recovery code via RecoveryCodeService
        var recoveryService = new RecoveryCodeService("0123456789ABCDEF0123456789ABCDEF");
        (string validCode, _) = recoveryService.IssueStationRecoveryCode(Environment.MachineName);

        bool invalidUnlock = await sessionMgr.TryAdminRecoveryUnlockAsync(
            "0000-BAD",
            (station, code) => Task.FromResult(recoveryService.VerifyStationRecoveryCode(station, code)));
        Assert.False(invalidUnlock);
        Assert.True(sessionMgr.IsLocked);

        bool validUnlock = await sessionMgr.TryAdminRecoveryUnlockAsync(
            validCode,
            (station, code) => Task.FromResult(recoveryService.VerifyStationRecoveryCode(station, code)));
        Assert.True(validUnlock);
        Assert.False(sessionMgr.IsLocked);
    }

    [Fact]
    public async Task ClientLocks_AreIsolatedPerStation()
    {
        var trackerPc1 = new FakeProcessTracker();
        var trackerPc2 = new FakeProcessTracker();

        var pc1Session = new LocalGameSessionManager(trackerPc1, Path.Combine(_tempRoot, "station-01"));
        var pc2Session = new LocalGameSessionManager(trackerPc2, Path.Combine(_tempRoot, "station-02"));

        var gameA = new GameEntry { Id = Guid.NewGuid(), Name = "Game A" };
        var gameB = new GameEntry { Id = Guid.NewGuid(), Name = "Game B" };

        (bool acquiredPc1, _, _) = await pc1Session.TryAcquireAsync(gameA, new[] { "gamea" });
        (bool acquiredPc2, _, _) = await pc2Session.TryAcquireAsync(gameB, new[] { "gameb" });

        Assert.True(acquiredPc1);
        Assert.True(acquiredPc2);
        Assert.Equal("Game A", pc1Session.CurrentSession?.GameName);
        Assert.Equal("Game B", pc2Session.CurrentSession?.GameName);
    }

    [Fact]
    public async Task SteamShellUriHandoff_WaitsForNamedGameAndUnlocksAfterExit()
    {
        var tracker = new ShellUriFakeProcessTracker();
        var sessionMgr = new LocalGameSessionManager(tracker, Path.Combine(_tempRoot, "pc-steam-uri"));
        var launchService = new GameLaunchService(
            new ClientLaunchPaths { SteamExecutablePath = @"C:\Missing\steam.exe" },
            sessionMgr,
            tracker,
            pollInterval: TimeSpan.FromMilliseconds(20),
            launcherHandoffWindow: TimeSpan.FromMilliseconds(400));

        var steamGame = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "Counter-Strike 2",
            LaunchType = LaunchType.Steam,
            ExternalAppId = "730",
            ProcessNameToMonitor = "cs2"
        };

        bool restored = false;
        Task<LaunchOutcome> launchTask = launchService.LaunchGameAsync(steamGame, () => { }, () => restored = true);

        await Task.Delay(40);
        Assert.True(sessionMgr.IsLocked);

        // Steam boots cs2.exe after initial URI handoff
        int cs2Pid = 4100;
        tracker.RunningPids[cs2Pid] = true;
        tracker.NamedPids["cs2"] = new List<int> { cs2Pid };

        await Task.Delay(80);
        Assert.True(sessionMgr.IsLocked);
        Assert.False(restored);

        // Game exits
        tracker.RunningPids[cs2Pid] = false;
        LaunchOutcome outcome = await launchTask;
        Assert.True(outcome.Success);
        Assert.True(restored);
        Assert.False(sessionMgr.IsLocked);
    }

    [Fact]
    public async Task ExternalLauncher_FailsClosedWhenStubExitsWithNoChildOrNamedProcess()
    {
        var tracker = new FakeProcessTracker();
        var sessionMgr = new LocalGameSessionManager(tracker, Path.Combine(_tempRoot, "pc-ext-fail"));
        var launchService = new GameLaunchService(
            new ClientLaunchPaths(),
            sessionMgr,
            tracker,
            pollInterval: TimeSpan.FromMilliseconds(20),
            launcherHandoffWindow: TimeSpan.FromMilliseconds(120));

        var externalGame = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "External IPC Game",
            LaunchType = LaunchType.ExternalLauncher,
            ExecutablePath = @"\\CCBOOT-SRV\Games\Launcher\launcher.exe"
        };

        Task<LaunchOutcome> launchTask = launchService.LaunchGameAsync(externalGame, () => { }, () => { });
        await Task.Delay(30);

        // IPC stub signals existing launcher and exits immediately without spawning a child
        foreach (int pid in tracker.RunningPids.Keys)
        {
            tracker.RunningPids[pid] = false;
        }

        LaunchOutcome outcome = await launchTask;
        Assert.False(outcome.Success);
        Assert.True(outcome.RequiresAdminRecovery);
        Assert.True(sessionMgr.IsLocked);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }

    private sealed class ShellUriFakeProcessTracker : IProcessTracker
    {
        public ConcurrentDictionary<int, bool> RunningPids { get; } = new();
        public ConcurrentDictionary<string, List<int>> NamedPids { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool FileExists(string path) => !path.EndsWith("steam.exe", StringComparison.OrdinalIgnoreCase);

        public TrackedProcessHandle? StartProcess(ProcessStartInfo startInfo) => null;

        public IReadOnlyList<int> GetChildProcessIds(int parentPid) => Array.Empty<int>();

        public bool IsProcessRunning(int pid) => RunningPids.TryGetValue(pid, out bool alive) && alive;

        public IReadOnlyList<int> GetProcessesByName(string processName) =>
            NamedPids.TryGetValue(processName, out List<int>? pids)
                ? pids.Where(IsProcessRunning).ToList()
                : Array.Empty<int>();
    }
}
