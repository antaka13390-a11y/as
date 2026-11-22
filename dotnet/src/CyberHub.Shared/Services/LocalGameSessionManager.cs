using System.Text.Json;
using CyberHub.Shared.Models;

namespace CyberHub.Shared.Services;

public sealed class LocalGameSessionManager
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _stateLock = new();
    private readonly string _lockFilePath;
    private readonly IProcessTracker _tracker;
    private LocalGameSessionState? _activeSession;

    public event EventHandler<LocalGameSessionState?>? SessionChanged;

    public LocalGameSessionManager(IProcessTracker tracker, string? customLockDirectory = null)
    {
        _tracker = tracker;
        string dir = customLockDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberHub");
        Directory.CreateDirectory(dir);
        _lockFilePath = Path.Combine(dir, "active-session.lock");

        RestorePersistedSessionState();
    }

    public LocalGameSessionState? CurrentSession
    {
        get
        {
            lock (_stateLock)
            {
                return _activeSession;
            }
        }
    }

    public bool IsLocked => CurrentSession is not null;

    public async Task<(bool Acquired, LocalGameSessionState? Session, string? BlockReason)> TryAcquireAsync(
        GameEntry game,
        IReadOnlyList<string> monitoredNames,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_activeSession is not null)
            {
                return (false, _activeSession, $"Active session already running: {_activeSession.GameName}");
            }

            var state = new LocalGameSessionState
            {
                SessionId = Guid.NewGuid(),
                GameId = game.Id,
                GameName = game.Name,
                StationName = Environment.MachineName,
                MonitoredProcessNames = monitoredNames.ToList(),
                StartedAtUtc = DateTimeOffset.UtcNow,
                RequiresAdminRecovery = false
            };

            lock (_stateLock)
            {
                _activeSession = state;
            }
            PersistLock(state);
            SessionChanged?.Invoke(this, state);
            return (true, state, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RegisterTrackedPidsAsync(int rootPid, IEnumerable<int> childPids, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_activeSession is null)
            {
                return;
            }

            _activeSession.RootProcessId ??= rootPid;
            var merged = new HashSet<int>(_activeSession.TrackedProcessIds) { rootPid };
            foreach (int pid in childPids)
            {
                merged.Add(pid);
            }
            _activeSession.TrackedProcessIds = merged.ToList();
            PersistLock(_activeSession);
            SessionChanged?.Invoke(this, _activeSession);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task MarkTrackingFailureAndKeepLockedAsync(string reason, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_activeSession is null)
            {
                _activeSession = new LocalGameSessionState
                {
                    GameName = "Unknown Session",
                    RequiresAdminRecovery = true,
                    FailureReason = reason
                };
            }
            else
            {
                _activeSession.RequiresAdminRecovery = true;
                _activeSession.FailureReason = reason;
            }

            PersistLock(_activeSession);
            SessionChanged?.Invoke(this, _activeSession);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task ReleaseAfterVerifiedExitAsync(CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            lock (_stateLock)
            {
                _activeSession = null;
            }
            DeleteLockFile();
            SessionChanged?.Invoke(this, null);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<bool> TryAdminRecoveryUnlockAsync(
        string recoveryCode,
        Func<string, string, Task<bool>> serverVerifyCallback,
        CancellationToken ct = default)
    {
        string station = Environment.MachineName;
        bool verified = await serverVerifyCallback(station, recoveryCode);
        if (!verified)
        {
            return false;
        }

        await ReleaseAfterVerifiedExitAsync(ct);
        return true;
    }

    private void RestorePersistedSessionState()
    {
        if (!File.Exists(_lockFilePath))
        {
            return;
        }

        try
        {
            string json = File.ReadAllText(_lockFilePath);
            LocalGameSessionState? saved = JsonSerializer.Deserialize<LocalGameSessionState>(json);
            if (saved is null)
            {
                return;
            }

            bool anyTrackedAlive =
                saved.TrackedProcessIds.Any(_tracker.IsProcessRunning) ||
                saved.MonitoredProcessNames.Any(name => _tracker.GetProcessesByName(name).Count > 0);

            if (anyTrackedAlive)
            {
                _activeSession = saved;
            }
            else
            {
                saved.RequiresAdminRecovery = true;
                saved.FailureReason = "Previous game session did not report a verified exit.";
                _activeSession = saved;
                PersistLock(saved);
            }
        }
        catch (Exception)
        {
            _activeSession = new LocalGameSessionState
            {
                GameName = "Locked Session",
                RequiresAdminRecovery = true,
                FailureReason = "Corrupted local session lock file; administrator recovery required."
            };
        }
    }

    private void PersistLock(LocalGameSessionState state)
    {
        string json = JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_lockFilePath, json);
    }

    private void DeleteLockFile()
    {
        if (File.Exists(_lockFilePath))
        {
            File.Delete(_lockFilePath);
        }
    }
}
