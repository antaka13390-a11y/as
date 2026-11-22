using CyberHub.ServerService.Configuration;
using CyberHub.Shared.Models;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;

namespace CyberHub.ServerService.Data;

public sealed class SqliteGameRepository
{
    private readonly string _connectionString;
    private readonly ServerOptions _options;
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public SqliteGameRepository(IOptions<ServerOptions> options)
    {
        _options = options.Value;
        string? dbDir = Path.GetDirectoryName(_options.DatabasePath);
        if (!string.IsNullOrWhiteSpace(dbDir))
        {
            Directory.CreateDirectory(dbDir);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _options.DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS StorageLocations (
                Id TEXT PRIMARY KEY,
                DisplayName TEXT NOT NULL,
                UncRootPath TEXT NOT NULL,
                Description TEXT NOT NULL,
                IsReadOnlyForClients INTEGER NOT NULL DEFAULT 1
            );

            CREATE TABLE IF NOT EXISTS Games (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                Category TEXT NOT NULL,
                Description TEXT NOT NULL,
                Developer TEXT NOT NULL,
                CoverImagePath TEXT NOT NULL,
                HeroImagePath TEXT NOT NULL,
                StorageLocationId TEXT NOT NULL,
                ExecutablePath TEXT NOT NULL,
                LaunchArguments TEXT NOT NULL,
                LaunchType INTEGER NOT NULL,
                ExternalAppId TEXT NOT NULL,
                ProcessNameToMonitor TEXT NOT NULL,
                IsVisible INTEGER NOT NULL DEFAULT 1,
                IsFeatured INTEGER NOT NULL DEFAULT 0,
                SortOrder INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdminConfig (
                ConfigKey TEXT PRIMARY KEY,
                ConfigValue TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS AdminAuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                Action TEXT NOT NULL,
                TargetGameId TEXT NOT NULL,
                TargetGameName TEXT NOT NULL,
                WorkstationName TEXT NOT NULL,
                SourceIp TEXT NOT NULL,
                Details TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS CatalogMetadata (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                CatalogVersion INTEGER NOT NULL DEFAULT 1,
                LastPublishedUtc TEXT NOT NULL,
                LastBackupUtc TEXT NOT NULL DEFAULT ''
            );

            INSERT OR IGNORE INTO CatalogMetadata (Id, CatalogVersion, LastPublishedUtc, LastBackupUtc)
            VALUES (1, 1, @Now, '');
        ";
        cmd.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        await SeedInitialStorageAndGamesIfEmptyAsync(conn, ct);
    }

    private async Task SeedInitialStorageAndGamesIfEmptyAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM StorageLocations;";
        long storageCount = Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct));

        if (storageCount == 0)
        {
            var defaultLocations = new[]
            {
                new StorageLocation
                {
                    Id = "ccboot-primary",
                    DisplayName = "CCBoot Game Disk (Primary)",
                    UncRootPath = @"\\CCBOOT-SRV\Games\CCBoot",
                    Description = "Primary CCBoot diskless iSCSI game image share",
                    IsReadOnlyForClients = true
                },
                new StorageLocation
                {
                    Id = "smb-steam",
                    DisplayName = "Server Steam Library Share",
                    UncRootPath = @"\\CCBOOT-SRV\Games\SteamLibrary\steamapps\common",
                    Description = "Windows Server 2022 SMB read-only Steam game vault",
                    IsReadOnlyForClients = true
                },
                new StorageLocation
                {
                    Id = "smb-riot",
                    DisplayName = "Riot & Competitive Share",
                    UncRootPath = @"\\CCBOOT-SRV\Games\Riot Games",
                    Description = "Shared competitive esports game directory",
                    IsReadOnlyForClients = true
                }
            };

            foreach (StorageLocation loc in defaultLocations)
            {
                await using var ins = conn.CreateCommand();
                ins.CommandText = @"
                    INSERT INTO StorageLocations (Id, DisplayName, UncRootPath, Description, IsReadOnlyForClients)
                    VALUES (@Id, @DisplayName, @UncRootPath, @Description, @IsReadOnly);";
                ins.Parameters.AddWithValue("@Id", loc.Id);
                ins.Parameters.AddWithValue("@DisplayName", loc.DisplayName);
                ins.Parameters.AddWithValue("@UncRootPath", loc.UncRootPath);
                ins.Parameters.AddWithValue("@Description", loc.Description);
                ins.Parameters.AddWithValue("@IsReadOnly", loc.IsReadOnlyForClients ? 1 : 0);
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
    }

    public async Task<string?> GetAdminPasswordHashAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ConfigValue FROM AdminConfig WHERE ConfigKey = 'AdminPasswordHash' LIMIT 1;";
        object? result = await cmd.ExecuteScalarAsync(ct);
        return result as string;
    }

    public async Task<bool> SetAdminPasswordHashIfUnsetAsync(string passwordHash, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT OR IGNORE INTO AdminConfig (ConfigKey, ConfigValue, UpdatedAtUtc)
                VALUES ('AdminPasswordHash', @Hash, @Now);";
            cmd.Parameters.AddWithValue("@Hash", passwordHash);
            cmd.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToString("O"));
            int affected = await cmd.ExecuteNonQueryAsync(ct);
            return affected > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<StorageLocation>> GetStorageLocationsAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, DisplayName, UncRootPath, Description, IsReadOnlyForClients FROM StorageLocations ORDER BY DisplayName;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var items = new List<StorageLocation>();
        while (await reader.ReadAsync(ct))
        {
            items.Add(new StorageLocation
            {
                Id = reader.GetString(0),
                DisplayName = reader.GetString(1),
                UncRootPath = reader.GetString(2),
                Description = reader.GetString(3),
                IsReadOnlyForClients = reader.GetInt32(4) == 1
            });
        }
        return items;
    }

    public async Task<List<GameEntry>> GetGamesAsync(bool includeHidden, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = includeHidden
            ? "SELECT Id, Name, Category, Description, Developer, CoverImagePath, HeroImagePath, StorageLocationId, ExecutablePath, LaunchArguments, LaunchType, ExternalAppId, ProcessNameToMonitor, IsVisible, IsFeatured, SortOrder, CreatedAtUtc, UpdatedAtUtc FROM Games ORDER BY SortOrder ASC, Name ASC;"
            : "SELECT Id, Name, Category, Description, Developer, CoverImagePath, HeroImagePath, StorageLocationId, ExecutablePath, LaunchArguments, LaunchType, ExternalAppId, ProcessNameToMonitor, IsVisible, IsFeatured, SortOrder, CreatedAtUtc, UpdatedAtUtc FROM Games WHERE IsVisible = 1 ORDER BY SortOrder ASC, Name ASC;";

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var games = new List<GameEntry>();
        while (await reader.ReadAsync(ct))
        {
            games.Add(MapGame(reader));
        }
        return games;
    }

    public async Task<GameEntry?> GetGameByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Category, Description, Developer, CoverImagePath, HeroImagePath, StorageLocationId, ExecutablePath, LaunchArguments, LaunchType, ExternalAppId, ProcessNameToMonitor, IsVisible, IsFeatured, SortOrder, CreatedAtUtc, UpdatedAtUtc FROM Games WHERE Id = @Id LIMIT 1;";
        cmd.Parameters.AddWithValue("@Id", id.ToString());

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return MapGame(reader);
        }
        return null;
    }

    public async Task<GameEntry> UpsertGameAsync(GameEntry game, bool isNew, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;

            if (isNew)
            {
                cmd.CommandText = @"
                    INSERT INTO Games (Id, Name, Category, Description, Developer, CoverImagePath, HeroImagePath, StorageLocationId, ExecutablePath, LaunchArguments, LaunchType, ExternalAppId, ProcessNameToMonitor, IsVisible, IsFeatured, SortOrder, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@Id, @Name, @Category, @Description, @Developer, @CoverImagePath, @HeroImagePath, @StorageLocationId, @ExecutablePath, @LaunchArguments, @LaunchType, @ExternalAppId, @ProcessNameToMonitor, @IsVisible, @IsFeatured, @SortOrder, @CreatedAtUtc, @UpdatedAtUtc);";
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE Games SET
                        Name = @Name,
                        Category = @Category,
                        Description = @Description,
                        Developer = @Developer,
                        CoverImagePath = @CoverImagePath,
                        HeroImagePath = @HeroImagePath,
                        StorageLocationId = @StorageLocationId,
                        ExecutablePath = @ExecutablePath,
                        LaunchArguments = @LaunchArguments,
                        LaunchType = @LaunchType,
                        ExternalAppId = @ExternalAppId,
                        ProcessNameToMonitor = @ProcessNameToMonitor,
                        IsVisible = @IsVisible,
                        IsFeatured = @IsFeatured,
                        SortOrder = @SortOrder,
                        UpdatedAtUtc = @UpdatedAtUtc
                    WHERE Id = @Id;";
            }

            cmd.Parameters.AddWithValue("@Id", game.Id.ToString());
            cmd.Parameters.AddWithValue("@Name", game.Name);
            cmd.Parameters.AddWithValue("@Category", game.Category);
            cmd.Parameters.AddWithValue("@Description", game.Description);
            cmd.Parameters.AddWithValue("@Developer", game.Developer);
            cmd.Parameters.AddWithValue("@CoverImagePath", game.CoverImagePath);
            cmd.Parameters.AddWithValue("@HeroImagePath", game.HeroImagePath);
            cmd.Parameters.AddWithValue("@StorageLocationId", game.StorageLocationId);
            cmd.Parameters.AddWithValue("@ExecutablePath", game.ExecutablePath);
            cmd.Parameters.AddWithValue("@LaunchArguments", game.LaunchArguments);
            cmd.Parameters.AddWithValue("@LaunchType", (int)game.LaunchType);
            cmd.Parameters.AddWithValue("@ExternalAppId", game.ExternalAppId);
            cmd.Parameters.AddWithValue("@ProcessNameToMonitor", game.ProcessNameToMonitor);
            cmd.Parameters.AddWithValue("@IsVisible", game.IsVisible ? 1 : 0);
            cmd.Parameters.AddWithValue("@IsFeatured", game.IsFeatured ? 1 : 0);
            cmd.Parameters.AddWithValue("@SortOrder", game.SortOrder);
            cmd.Parameters.AddWithValue("@CreatedAtUtc", game.CreatedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@UpdatedAtUtc", game.UpdatedAtUtc.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);

            await IncrementCatalogVersionAsync(conn, (SqliteTransaction)tx, ct);
            await tx.CommitAsync(ct);
            return game;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Removes ONLY the database library record. Never touches or deletes actual game files or directories.
    /// </summary>
    public async Task<bool> DeleteGameRecordOnlyAsync(Guid id, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var tx = await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = (SqliteTransaction)tx;
            cmd.CommandText = "DELETE FROM Games WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id.ToString());

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected > 0)
            {
                await IncrementCatalogVersionAsync(conn, (SqliteTransaction)tx, ct);
            }

            await tx.CommitAsync(ct);
            return affected > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task AppendAuditLogAsync(AdminAuditLog entry, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO AdminAuditLogs (TimestampUtc, Action, TargetGameId, TargetGameName, WorkstationName, SourceIp, Details)
                VALUES (@Ts, @Action, @TargetId, @TargetName, @Workstation, @Ip, @Details);";
            cmd.Parameters.AddWithValue("@Ts", entry.TimestampUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@Action", entry.Action);
            cmd.Parameters.AddWithValue("@TargetId", entry.TargetGameId);
            cmd.Parameters.AddWithValue("@TargetName", entry.TargetGameName);
            cmd.Parameters.AddWithValue("@Workstation", entry.WorkstationName);
            cmd.Parameters.AddWithValue("@Ip", entry.SourceIp);
            cmd.Parameters.AddWithValue("@Details", entry.Details);

            await cmd.ExecuteNonQueryAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<AdminAuditLog>> GetAuditLogsAsync(int limit = 200, CancellationToken ct = default)
    {
        int safeLimit = Math.Clamp(limit, 1, 1000);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, TimestampUtc, Action, TargetGameId, TargetGameName, WorkstationName, SourceIp, Details FROM AdminAuditLogs ORDER BY Id DESC LIMIT @Limit;";
        cmd.Parameters.AddWithValue("@Limit", safeLimit);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        var logs = new List<AdminAuditLog>();
        while (await reader.ReadAsync(ct))
        {
            logs.Add(new AdminAuditLog
            {
                Id = reader.GetInt64(0),
                TimestampUtc = DateTimeOffset.Parse(reader.GetString(1)),
                Action = reader.GetString(2),
                TargetGameId = reader.GetString(3),
                TargetGameName = reader.GetString(4),
                WorkstationName = reader.GetString(5),
                SourceIp = reader.GetString(6),
                Details = reader.GetString(7)
            });
        }
        return logs;
    }

    public async Task<(long Version, string LastBackupUtc)> GetCatalogMetadataAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CatalogVersion, LastBackupUtc FROM CatalogMetadata WHERE Id = 1;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return (reader.GetInt64(0), reader.GetString(1));
        }
        return (1, string.Empty);
    }

    public async Task<BackupResult> CreateOnlineBackupAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_options.BackupDirectory);
        string fileName = $"cyberhub-backup-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}.db";
        string backupPath = Path.Combine(_options.BackupDirectory, fileName);

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var source = new SqliteConnection(_connectionString);
            await source.OpenAsync(ct);

            string destConnStr = new SqliteConnectionStringBuilder
            {
                DataSource = backupPath,
                Mode = SqliteOpenMode.ReadWriteCreate
            }.ToString();

            await using (var destination = new SqliteConnection(destConnStr))
            {
                await destination.OpenAsync(ct);
                source.BackupDatabase(destination);
            }

            string nowIso = DateTimeOffset.UtcNow.ToString("O");
            await using var updateCmd = source.CreateCommand();
            updateCmd.CommandText = "UPDATE CatalogMetadata SET LastBackupUtc = @Now WHERE Id = 1;";
            updateCmd.Parameters.AddWithValue("@Now", nowIso);
            await updateCmd.ExecuteNonQueryAsync(ct);

            var fileInfo = new FileInfo(backupPath);
            return new BackupResult
            {
                BackupFilePath = backupPath,
                SizeBytes = fileInfo.Length,
                CreatedAtUtc = DateTimeOffset.UtcNow
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private static async Task IncrementCatalogVersionAsync(SqliteConnection conn, SqliteTransaction tx, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "UPDATE CatalogMetadata SET CatalogVersion = CatalogVersion + 1, LastPublishedUtc = @Now WHERE Id = 1;";
        cmd.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static GameEntry MapGame(SqliteDataReader reader) => new()
    {
        Id = Guid.Parse(reader.GetString(0)),
        Name = reader.GetString(1),
        Category = reader.GetString(2),
        Description = reader.GetString(3),
        Developer = reader.GetString(4),
        CoverImagePath = reader.GetString(5),
        HeroImagePath = reader.GetString(6),
        StorageLocationId = reader.GetString(7),
        ExecutablePath = reader.GetString(8),
        LaunchArguments = reader.GetString(9),
        LaunchType = (LaunchType)reader.GetInt32(10),
        ExternalAppId = reader.GetString(11),
        ProcessNameToMonitor = reader.GetString(12),
        IsVisible = reader.GetInt32(13) == 1,
        IsFeatured = reader.GetInt32(14) == 1,
        SortOrder = reader.GetInt32(15),
        CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(16)),
        UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(17))
    };
}
