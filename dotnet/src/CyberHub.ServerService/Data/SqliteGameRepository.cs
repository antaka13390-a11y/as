using System.Text.Json;
using CyberHub.ServerService.Configuration;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using CyberHub.Shared.Services;
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
                NetworkMode INTEGER NOT NULL DEFAULT 0,
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

            CREATE TABLE IF NOT EXISTS Categories (
                Id TEXT PRIMARY KEY,
                Slug TEXT NOT NULL UNIQUE,
                NameEn TEXT NOT NULL,
                NameAr TEXT NOT NULL,
                IconKey TEXT NOT NULL DEFAULT 'gamepad',
                BannerImagePath TEXT NOT NULL DEFAULT '',
                DisplayOrder INTEGER NOT NULL DEFAULT 0,
                IsSystem INTEGER NOT NULL DEFAULT 0,
                FilterType INTEGER NOT NULL DEFAULT 0,
                CreatedAtUtc TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS GameCategories (
                GameId TEXT NOT NULL,
                CategoryId TEXT NOT NULL,
                PRIMARY KEY (GameId, CategoryId),
                FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE,
                FOREIGN KEY (CategoryId) REFERENCES Categories(Id) ON DELETE CASCADE
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

            CREATE TABLE IF NOT EXISTS SchemaMigrations (
                Version INTEGER PRIMARY KEY,
                Description TEXT NOT NULL,
                AppliedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Themes (
                Id TEXT PRIMARY KEY,
                Name TEXT NOT NULL,
                PresetKey TEXT NOT NULL DEFAULT 'Custom',
                IsDefault INTEGER NOT NULL DEFAULT 0,
                IsPublished INTEGER NOT NULL DEFAULT 0,
                Version INTEGER NOT NULL DEFAULT 1,
                ConfigJson TEXT NOT NULL,
                UpdatedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ThemeMetadata (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                ActiveThemeId TEXT NOT NULL,
                PublishedVersion INTEGER NOT NULL DEFAULT 1,
                LastPublishedUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ThemeAssets (
                Id TEXT PRIMARY KEY,
                FileName TEXT NOT NULL UNIQUE,
                OriginalFileName TEXT NOT NULL,
                MediaKind TEXT NOT NULL,
                ContentType TEXT NOT NULL,
                SizeBytes INTEGER NOT NULL,
                RelativeUrl TEXT NOT NULL,
                UploadedAtUtc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS ThemeAuditLogs (
                Id INTEGER PRIMARY KEY AUTOINCREMENT,
                TimestampUtc TEXT NOT NULL,
                Action TEXT NOT NULL,
                ThemeId TEXT NOT NULL,
                ThemeName TEXT NOT NULL,
                WorkstationName TEXT NOT NULL,
                SourceIp TEXT NOT NULL,
                Details TEXT NOT NULL
            );

            INSERT OR IGNORE INTO CatalogMetadata (Id, CatalogVersion, LastPublishedUtc, LastBackupUtc)
            VALUES (1, 1, @Now, '');

            INSERT OR IGNORE INTO AdminConfig (ConfigKey, ConfigValue, UpdatedAtUtc)
            VALUES ('TokenEpoch', '1', @Now);
        ";
        cmd.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToString("O"));
        await cmd.ExecuteNonQueryAsync(ct);

        await ApplySafeMigrationsAsync(conn, ct);
        await SeedInitialStorageAndCategoriesAsync(conn, ct);
        await SeedInitialThemesAsync(conn, ct);
        await MigrateLegacyGameCategoriesAsync(conn, ct);
    }

    private async Task ApplySafeMigrationsAsync(SqliteConnection conn, CancellationToken ct)
    {
        // Migration v1: Ensure NetworkMode column exists on pre-existing Games tables without touching records
        bool hasNetworkMode = false;
        await using (var pragmaCmd = conn.CreateCommand())
        {
            pragmaCmd.CommandText = "PRAGMA table_info(Games);";
            await using var reader = await pragmaCmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (string.Equals(reader.GetString(1), "NetworkMode", StringComparison.OrdinalIgnoreCase))
                {
                    hasNetworkMode = true;
                    break;
                }
            }
        }

        if (!hasNetworkMode)
        {
            await using var alterCmd = conn.CreateCommand();
            alterCmd.CommandText = "ALTER TABLE Games ADD COLUMN NetworkMode INTEGER NOT NULL DEFAULT 0;";
            await alterCmd.ExecuteNonQueryAsync(ct);
        }

        await using var migCmd = conn.CreateCommand();
        migCmd.CommandText = @"
            INSERT OR IGNORE INTO SchemaMigrations (Version, Description, AppliedAtUtc)
            VALUES (2, 'Added Categories, GameCategories junction table, and Games.NetworkMode', @Now);
            INSERT OR IGNORE INTO SchemaMigrations (Version, Description, AppliedAtUtc)
            VALUES (3, 'Added Themes, ThemeMetadata, ThemeAssets, ThemeAuditLogs, and AdminConfig.TokenEpoch', @Now);";
        migCmd.Parameters.AddWithValue("@Now", DateTimeOffset.UtcNow.ToString("O"));
        await migCmd.ExecuteNonQueryAsync(ct);
    }

    private async Task SeedInitialStorageAndCategoriesAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var storageCountCmd = conn.CreateCommand();
        storageCountCmd.CommandText = "SELECT COUNT(*) FROM StorageLocations;";
        if (Convert.ToInt64(await storageCountCmd.ExecuteScalarAsync(ct)) == 0)
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

        await using var catCountCmd = conn.CreateCommand();
        catCountCmd.CommandText = "SELECT COUNT(*) FROM Categories;";
        if (Convert.ToInt64(await catCountCmd.ExecuteScalarAsync(ct)) == 0)
        {
            string now = DateTimeOffset.UtcNow.ToString("O");
            var defaults = new[]
            {
                new CategoryEntry { Id = "cat-all", Slug = "all-games", NameEn = "All Games", NameAr = "جميع الألعاب", IconKey = "grid", DisplayOrder = 1, IsSystem = true, FilterType = CategoryFilterType.AllGames },
                new CategoryEntry { Id = "cat-online", Slug = "online-games", NameEn = "Online Games", NameAr = "ألعاب أونلاين", IconKey = "globe", DisplayOrder = 2, IsSystem = true, FilterType = CategoryFilterType.OnlineGames },
                new CategoryEntry { Id = "cat-offline", Slug = "offline-games", NameEn = "Offline Games", NameAr = "ألعاب أوفلاين", IconKey = "hard-drive", DisplayOrder = 3, IsSystem = true, FilterType = CategoryFilterType.OfflineGames },
                new CategoryEntry { Id = "cat-action", Slug = "action", NameEn = "Action", NameAr = "أكشن", IconKey = "flame", DisplayOrder = 4, IsSystem = false, FilterType = CategoryFilterType.Custom },
                new CategoryEntry { Id = "cat-shooting", Slug = "shooting", NameEn = "Shooting", NameAr = "تصويب", IconKey = "crosshair", DisplayOrder = 5, IsSystem = false, FilterType = CategoryFilterType.Custom },
                new CategoryEntry { Id = "cat-sports", Slug = "sports", NameEn = "Sports", NameAr = "رياضة", IconKey = "trophy", DisplayOrder = 6, IsSystem = false, FilterType = CategoryFilterType.Custom },
                new CategoryEntry { Id = "cat-racing", Slug = "racing", NameEn = "Racing", NameAr = "سباقات", IconKey = "gauge", DisplayOrder = 7, IsSystem = false, FilterType = CategoryFilterType.Custom },
                new CategoryEntry { Id = "cat-adventure", Slug = "adventure", NameEn = "Adventure", NameAr = "مغامرات", IconKey = "compass", DisplayOrder = 8, IsSystem = false, FilterType = CategoryFilterType.Custom }
            };

            foreach (CategoryEntry cat in defaults)
            {
                await using var ins = conn.CreateCommand();
                ins.CommandText = @"
                    INSERT INTO Categories (Id, Slug, NameEn, NameAr, IconKey, BannerImagePath, DisplayOrder, IsSystem, FilterType, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@Id, @Slug, @NameEn, @NameAr, @IconKey, '', @Order, @IsSystem, @FilterType, @Now, @Now);";
                ins.Parameters.AddWithValue("@Id", cat.Id);
                ins.Parameters.AddWithValue("@Slug", cat.Slug);
                ins.Parameters.AddWithValue("@NameEn", cat.NameEn);
                ins.Parameters.AddWithValue("@NameAr", cat.NameAr);
                ins.Parameters.AddWithValue("@IconKey", cat.IconKey);
                ins.Parameters.AddWithValue("@Order", cat.DisplayOrder);
                ins.Parameters.AddWithValue("@IsSystem", cat.IsSystem ? 1 : 0);
                ins.Parameters.AddWithValue("@FilterType", (int)cat.FilterType);
                ins.Parameters.AddWithValue("@Now", now);
                await ins.ExecuteNonQueryAsync(ct);
            }
        }
    }

    private async Task MigrateLegacyGameCategoriesAsync(SqliteConnection conn, CancellationToken ct)
    {
        // Link any existing Games lacking GameCategories rows without discarding existing records
        var unlinkedGames = new List<(string GameId, string LegacyCategory)>();
        await using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT g.Id, g.Category
                FROM Games g
                WHERE NOT EXISTS (SELECT 1 FROM GameCategories gc WHERE gc.GameId = g.Id);";
            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                unlinkedGames.Add((reader.GetString(0), reader.GetString(1)));
            }
        }

        if (unlinkedGames.Count == 0)
        {
            return;
        }

        List<CategoryEntry> categories = await GetCategoriesInternalAsync(conn, ct);
        foreach ((string gameId, string legacyCat) in unlinkedGames)
        {
            CategoryEntry? matched = categories.FirstOrDefault(c =>
                !c.IsSystem &&
                (string.Equals(c.NameEn, legacyCat, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(c.NameAr, legacyCat, StringComparison.OrdinalIgnoreCase)));

            matched ??= categories.FirstOrDefault(c => !c.IsSystem);
            if (matched is not null)
            {
                await using var ins = conn.CreateCommand();
                ins.CommandText = "INSERT OR IGNORE INTO GameCategories (GameId, CategoryId) VALUES (@GameId, @CategoryId);";
                ins.Parameters.AddWithValue("@GameId", gameId);
                ins.Parameters.AddWithValue("@CategoryId", matched.Id);
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
        return await cmd.ExecuteScalarAsync(ct) as string;
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
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<long> GetTokenEpochAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ConfigValue FROM AdminConfig WHERE ConfigKey = 'TokenEpoch' LIMIT 1;";
        object? scalar = await cmd.ExecuteScalarAsync(ct);
        return long.TryParse(scalar as string, out long epoch) && epoch >= 1 ? epoch : 1;
    }

    public async Task<(bool Success, string Error)> ChangeAdminPasswordAsync(
        string currentPassword,
        string newPassword,
        TokenService tokenService,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CYBERHUB_ADMIN_HASH")))
        {
            return (false, "Password changes are blocked while CYBERHUB_ADMIN_HASH environment override is configured.");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 10)
        {
            return (false, "New password must be at least 10 characters.");
        }

        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            string? existingHash;
            await using (var readCmd = conn.CreateCommand())
            {
                readCmd.CommandText = "SELECT ConfigValue FROM AdminConfig WHERE ConfigKey = 'AdminPasswordHash' LIMIT 1;";
                existingHash = await readCmd.ExecuteScalarAsync(ct) as string;
            }

            if (string.IsNullOrWhiteSpace(existingHash))
            {
                return (false, "Administrator account is not configured yet.");
            }

            if (!PasswordHasher.VerifyPassword(currentPassword, existingHash))
            {
                await AppendAuditLogInternalAsync(conn, null, new AdminAuditLog
                {
                    Action = "ADMIN_PASSWORD_CHANGE_FAILED",
                    WorkstationName = workstation,
                    SourceIp = sourceIp,
                    Details = "Rejected password change due to invalid current password."
                }, ct);
                return (false, "Current administrator password is incorrect.");
            }

            string newHash = PasswordHasher.HashPassword(newPassword);
            string nowIso = DateTimeOffset.UtcNow.ToString("O");

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            await using (var updPwd = conn.CreateCommand())
            {
                updPwd.Transaction = tx;
                updPwd.CommandText = @"
                    INSERT INTO AdminConfig (ConfigKey, ConfigValue, UpdatedAtUtc)
                    VALUES ('AdminPasswordHash', @Hash, @Now)
                    ON CONFLICT(ConfigKey) DO UPDATE SET ConfigValue = excluded.ConfigValue, UpdatedAtUtc = excluded.UpdatedAtUtc;";
                updPwd.Parameters.AddWithValue("@Hash", newHash);
                updPwd.Parameters.AddWithValue("@Now", nowIso);
                await updPwd.ExecuteNonQueryAsync(ct);
            }

            long nextEpoch = 2;
            await using (var readEpoch = conn.CreateCommand())
            {
                readEpoch.Transaction = tx;
                readEpoch.CommandText = "SELECT ConfigValue FROM AdminConfig WHERE ConfigKey = 'TokenEpoch' LIMIT 1;";
                if (long.TryParse(await readEpoch.ExecuteScalarAsync(ct) as string, out long currentEpoch) && currentEpoch >= 1)
                {
                    nextEpoch = currentEpoch + 1;
                }
            }

            await using (var updEpoch = conn.CreateCommand())
            {
                updEpoch.Transaction = tx;
                updEpoch.CommandText = @"
                    INSERT INTO AdminConfig (ConfigKey, ConfigValue, UpdatedAtUtc)
                    VALUES ('TokenEpoch', @Epoch, @Now)
                    ON CONFLICT(ConfigKey) DO UPDATE SET ConfigValue = excluded.ConfigValue, UpdatedAtUtc = excluded.UpdatedAtUtc;";
                updEpoch.Parameters.AddWithValue("@Epoch", nextEpoch.ToString());
                updEpoch.Parameters.AddWithValue("@Now", nowIso);
                await updEpoch.ExecuteNonQueryAsync(ct);
            }

            await AppendAuditLogInternalAsync(conn, tx, new AdminAuditLog
            {
                Action = "ADMIN_PASSWORD_CHANGED",
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Administrator password updated and all active sessions revoked (token epoch {nextEpoch})."
            }, ct);

            await tx.CommitAsync(ct);
            tokenService.SetMinimumValidEpoch(nextEpoch);
            return (true, string.Empty);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<long> IncrementTokenEpochAsync(string workstation, string sourceIp, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            long nextEpoch = 2;
            await using (var readEpoch = conn.CreateCommand())
            {
                readEpoch.Transaction = tx;
                readEpoch.CommandText = "SELECT ConfigValue FROM AdminConfig WHERE ConfigKey = 'TokenEpoch' LIMIT 1;";
                if (long.TryParse(await readEpoch.ExecuteScalarAsync(ct) as string, out long currentEpoch) && currentEpoch >= 1)
                {
                    nextEpoch = currentEpoch + 1;
                }
            }

            string nowIso = DateTimeOffset.UtcNow.ToString("O");
            await using (var updEpoch = conn.CreateCommand())
            {
                updEpoch.Transaction = tx;
                updEpoch.CommandText = @"
                    INSERT INTO AdminConfig (ConfigKey, ConfigValue, UpdatedAtUtc)
                    VALUES ('TokenEpoch', @Epoch, @Now)
                    ON CONFLICT(ConfigKey) DO UPDATE SET ConfigValue = excluded.ConfigValue, UpdatedAtUtc = excluded.UpdatedAtUtc;";
                updEpoch.Parameters.AddWithValue("@Epoch", nextEpoch.ToString());
                updEpoch.Parameters.AddWithValue("@Now", nowIso);
                await updEpoch.ExecuteNonQueryAsync(ct);
            }

            await AppendAuditLogInternalAsync(conn, tx, new AdminAuditLog
            {
                Action = "ADMIN_LOGOUT",
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Administrator logged out cleanly; tokens invalidated (epoch {nextEpoch})."
            }, ct);

            await tx.CommitAsync(ct);
            return nextEpoch;
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

    public async Task<StorageLocation> UpsertStorageLocationAsync(StorageLocation location, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            string id = string.IsNullOrWhiteSpace(location.Id)
                ? $"loc-{Guid.NewGuid():N}"[..12]
                : location.Id.Trim();

            location.Id = id;
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO StorageLocations (Id, DisplayName, UncRootPath, Description, IsReadOnlyForClients)
                VALUES (@Id, @DisplayName, @UncRootPath, @Description, @IsReadOnly)
                ON CONFLICT(Id) DO UPDATE SET
                    DisplayName = excluded.DisplayName,
                    UncRootPath = excluded.UncRootPath,
                    Description = excluded.Description,
                    IsReadOnlyForClients = excluded.IsReadOnlyForClients;";
            cmd.Parameters.AddWithValue("@Id", location.Id);
            cmd.Parameters.AddWithValue("@DisplayName", location.DisplayName.Trim());
            cmd.Parameters.AddWithValue("@UncRootPath", location.UncRootPath.Trim());
            cmd.Parameters.AddWithValue("@Description", location.Description.Trim());
            cmd.Parameters.AddWithValue("@IsReadOnly", location.IsReadOnlyForClients ? 1 : 0);
            await cmd.ExecuteNonQueryAsync(ct);
            return location;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<bool> DeleteStorageLocationAsync(string id, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM StorageLocations WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            return await cmd.ExecuteNonQueryAsync(ct) > 0;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<CategoryEntry>> GetCategoriesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await GetCategoriesInternalAsync(conn, ct);
    }

    private static async Task<List<CategoryEntry>> GetCategoriesInternalAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Slug, NameEn, NameAr, IconKey, BannerImagePath, DisplayOrder, IsSystem, FilterType, CreatedAtUtc, UpdatedAtUtc
            FROM Categories
            ORDER BY DisplayOrder ASC, NameEn ASC;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<CategoryEntry>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new CategoryEntry
            {
                Id = reader.GetString(0),
                Slug = reader.GetString(1),
                NameEn = reader.GetString(2),
                NameAr = reader.GetString(3),
                IconKey = reader.GetString(4),
                BannerImagePath = reader.GetString(5),
                DisplayOrder = reader.GetInt32(6),
                IsSystem = reader.GetInt32(7) == 1,
                FilterType = (CategoryFilterType)reader.GetInt32(8),
                CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(9)),
                UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(10))
            });
        }
        return list;
    }

    public async Task<CategoryEntry?> GetCategoryByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT Id, Slug, NameEn, NameAr, IconKey, BannerImagePath, DisplayOrder, IsSystem, FilterType, CreatedAtUtc, UpdatedAtUtc
            FROM Categories WHERE Id = @Id LIMIT 1;";
        cmd.Parameters.AddWithValue("@Id", id);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        return new CategoryEntry
        {
            Id = reader.GetString(0),
            Slug = reader.GetString(1),
            NameEn = reader.GetString(2),
            NameAr = reader.GetString(3),
            IconKey = reader.GetString(4),
            BannerImagePath = reader.GetString(5),
            DisplayOrder = reader.GetInt32(6),
            IsSystem = reader.GetInt32(7) == 1,
            FilterType = (CategoryFilterType)reader.GetInt32(8),
            CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(9)),
            UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(10))
        };
    }

    public async Task<CategoryEntry> UpsertCategoryAsync(CategoryEntry category, bool isNew, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;

            if (isNew)
            {
                if (string.IsNullOrWhiteSpace(category.Slug))
                {
                    category.Slug = UncPathValidator.GenerateSafeSlug(category.NameEn);
                }

                cmd.CommandText = @"
                    INSERT INTO Categories (Id, Slug, NameEn, NameAr, IconKey, BannerImagePath, DisplayOrder, IsSystem, FilterType, CreatedAtUtc, UpdatedAtUtc)
                    VALUES (@Id, @Slug, @NameEn, @NameAr, @IconKey, @Banner, @Order, 0, 0, @Created, @Updated);";
            }
            else
            {
                cmd.CommandText = @"
                    UPDATE Categories SET
                        NameEn = @NameEn,
                        NameAr = @NameAr,
                        IconKey = @IconKey,
                        BannerImagePath = @Banner,
                        DisplayOrder = @Order,
                        UpdatedAtUtc = @Updated
                    WHERE Id = @Id;";
            }

            cmd.Parameters.AddWithValue("@Id", category.Id);
            cmd.Parameters.AddWithValue("@Slug", category.Slug);
            cmd.Parameters.AddWithValue("@NameEn", category.NameEn);
            cmd.Parameters.AddWithValue("@NameAr", category.NameAr);
            cmd.Parameters.AddWithValue("@IconKey", category.IconKey);
            cmd.Parameters.AddWithValue("@Banner", category.BannerImagePath);
            cmd.Parameters.AddWithValue("@Order", category.DisplayOrder);
            cmd.Parameters.AddWithValue("@Created", category.CreatedAtUtc.ToString("O"));
            cmd.Parameters.AddWithValue("@Updated", category.UpdatedAtUtc.ToString("O"));

            await cmd.ExecuteNonQueryAsync(ct);
            await IncrementCatalogVersionAsync(conn, tx, ct);
            await tx.CommitAsync(ct);
            return category;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    /// <summary>
    /// Safely deletes a custom category and its junction links without deleting any game records or disk files.
    /// System categories (All Games, Online Games, Offline Games) cannot be deleted.
    /// </summary>
    public async Task<(bool Deleted, string Error)> DeleteCategorySafelyAsync(string categoryId, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);

            await using var checkCmd = conn.CreateCommand();
            checkCmd.CommandText = "SELECT IsSystem FROM Categories WHERE Id = @Id LIMIT 1;";
            checkCmd.Parameters.AddWithValue("@Id", categoryId);
            object? res = await checkCmd.ExecuteScalarAsync(ct);
            if (res is null)
            {
                return (false, "Category not found.");
            }
            if (Convert.ToInt32(res) == 1)
            {
                return (false, "Built-in system categories (All, Online, Offline) cannot be deleted.");
            }

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            await using (var delLinks = conn.CreateCommand())
            {
                delLinks.Transaction = tx;
                delLinks.CommandText = "DELETE FROM GameCategories WHERE CategoryId = @Id;";
                delLinks.Parameters.AddWithValue("@Id", categoryId);
                await delLinks.ExecuteNonQueryAsync(ct);
            }

            await using (var delCat = conn.CreateCommand())
            {
                delCat.Transaction = tx;
                delCat.CommandText = "DELETE FROM Categories WHERE Id = @Id AND IsSystem = 0;";
                delCat.Parameters.AddWithValue("@Id", categoryId);
                await delCat.ExecuteNonQueryAsync(ct);
            }

            await IncrementCatalogVersionAsync(conn, tx, ct);
            await tx.CommitAsync(ct);
            return (true, string.Empty);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<GameEntry>> GetGamesAsync(bool includeHidden, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = includeHidden
            ? "SELECT Id, Name, Category, NetworkMode, Description, Developer, CoverImagePath, HeroImagePath, StorageLocationId, ExecutablePath, LaunchArguments, LaunchType, ExternalAppId, ProcessNameToMonitor, IsVisible, IsFeatured, SortOrder, CreatedAtUtc, UpdatedAtUtc FROM Games ORDER BY SortOrder ASC, Name ASC;"
            : "SELECT Id, Name, Category, NetworkMode, Description, Developer, CoverImagePath, HeroImagePath, StorageLocationId, ExecutablePath, LaunchArguments, LaunchType, ExternalAppId, ProcessNameToMonitor, IsVisible, IsFeatured, SortOrder, CreatedAtUtc, UpdatedAtUtc FROM Games WHERE IsVisible = 1 ORDER BY SortOrder ASC, Name ASC;";

        var games = new List<GameEntry>();
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            while (await reader.ReadAsync(ct))
            {
                games.Add(MapGame(reader));
            }
        }

        if (games.Count > 0)
        {
            Dictionary<string, List<string>> map = await LoadGameCategoryIdsAsync(conn, ct);
            foreach (GameEntry game in games)
            {
                if (map.TryGetValue(game.Id.ToString(), out List<string>? ids))
                {
                    game.CategoryIds = ids;
                }
            }
        }

        return games;
    }

    public async Task<GameEntry?> GetGameByIdAsync(Guid id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, Name, Category, NetworkMode, Description, Developer, CoverImagePath, HeroImagePath, StorageLocationId, ExecutablePath, LaunchArguments, LaunchType, ExternalAppId, ProcessNameToMonitor, IsVisible, IsFeatured, SortOrder, CreatedAtUtc, UpdatedAtUtc FROM Games WHERE Id = @Id LIMIT 1;";
        cmd.Parameters.AddWithValue("@Id", id.ToString());

        GameEntry? game = null;
        await using (var reader = await cmd.ExecuteReaderAsync(ct))
        {
            if (await reader.ReadAsync(ct))
            {
                game = MapGame(reader);
            }
        }

        if (game is not null)
        {
            await using var catCmd = conn.CreateCommand();
            catCmd.CommandText = "SELECT CategoryId FROM GameCategories WHERE GameId = @Id;";
            catCmd.Parameters.AddWithValue("@Id", id.ToString());
            await using var catReader = await catCmd.ExecuteReaderAsync(ct);
            while (await catReader.ReadAsync(ct))
            {
                game.CategoryIds.Add(catReader.GetString(0));
            }
        }

        return game;
    }

    public async Task<GameEntry> UpsertGameAsync(GameEntry game, bool isNew, CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = isNew
                    ? @"
                        INSERT INTO Games (Id, Name, Category, NetworkMode, Description, Developer, CoverImagePath, HeroImagePath, StorageLocationId, ExecutablePath, LaunchArguments, LaunchType, ExternalAppId, ProcessNameToMonitor, IsVisible, IsFeatured, SortOrder, CreatedAtUtc, UpdatedAtUtc)
                        VALUES (@Id, @Name, @Category, @NetworkMode, @Description, @Developer, @CoverImagePath, @HeroImagePath, @StorageLocationId, @ExecutablePath, @LaunchArguments, @LaunchType, @ExternalAppId, @ProcessNameToMonitor, @IsVisible, @IsFeatured, @SortOrder, @CreatedAtUtc, @UpdatedAtUtc);"
                    : @"
                        UPDATE Games SET
                            Name = @Name,
                            Category = @Category,
                            NetworkMode = @NetworkMode,
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

                cmd.Parameters.AddWithValue("@Id", game.Id.ToString());
                cmd.Parameters.AddWithValue("@Name", game.Name);
                cmd.Parameters.AddWithValue("@Category", game.Category);
                cmd.Parameters.AddWithValue("@NetworkMode", (int)game.NetworkMode);
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
            }

            await using (var delLinks = conn.CreateCommand())
            {
                delLinks.Transaction = tx;
                delLinks.CommandText = "DELETE FROM GameCategories WHERE GameId = @GameId;";
                delLinks.Parameters.AddWithValue("@GameId", game.Id.ToString());
                await delLinks.ExecuteNonQueryAsync(ct);
            }

            foreach (string catId in game.CategoryIds.Distinct(StringComparer.Ordinal))
            {
                await using var insLink = conn.CreateCommand();
                insLink.Transaction = tx;
                insLink.CommandText = "INSERT OR IGNORE INTO GameCategories (GameId, CategoryId) VALUES (@GameId, @CategoryId);";
                insLink.Parameters.AddWithValue("@GameId", game.Id.ToString());
                insLink.Parameters.AddWithValue("@CategoryId", catId);
                await insLink.ExecuteNonQueryAsync(ct);
            }

            await IncrementCatalogVersionAsync(conn, tx, ct);
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

            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM Games WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id.ToString());

            int affected = await cmd.ExecuteNonQueryAsync(ct);
            if (affected > 0)
            {
                await IncrementCatalogVersionAsync(conn, tx, ct);
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

    private static async Task SeedInitialThemesAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var countCmd = conn.CreateCommand();
        countCmd.CommandText = "SELECT COUNT(*) FROM Themes;";
        if (Convert.ToInt64(await countCmd.ExecuteScalarAsync(ct)) == 0)
        {
            List<ThemeConfig> presets = ThemePresetFactory.CreateBuiltInPresets();
            string nowIso = DateTimeOffset.UtcNow.ToString("O");

            foreach (ThemeConfig preset in presets)
            {
                preset.UpdatedAtUtc = DateTimeOffset.UtcNow;
                string json = JsonSerializer.Serialize(preset);

                await using var ins = conn.CreateCommand();
                ins.CommandText = @"
                    INSERT INTO Themes (Id, Name, PresetKey, IsDefault, IsPublished, Version, ConfigJson, UpdatedAtUtc)
                    VALUES (@Id, @Name, @PresetKey, @IsDefault, @IsPublished, @Version, @ConfigJson, @UpdatedAtUtc);";
                ins.Parameters.AddWithValue("@Id", preset.Id);
                ins.Parameters.AddWithValue("@Name", preset.Name);
                ins.Parameters.AddWithValue("@PresetKey", preset.PresetKey);
                ins.Parameters.AddWithValue("@IsDefault", preset.IsDefault ? 1 : 0);
                ins.Parameters.AddWithValue("@IsPublished", preset.IsPublished ? 1 : 0);
                ins.Parameters.AddWithValue("@Version", preset.Version);
                ins.Parameters.AddWithValue("@ConfigJson", json);
                ins.Parameters.AddWithValue("@UpdatedAtUtc", nowIso);
                await ins.ExecuteNonQueryAsync(ct);
            }

            await using var metaIns = conn.CreateCommand();
            metaIns.CommandText = @"
                INSERT OR IGNORE INTO ThemeMetadata (Id, ActiveThemeId, PublishedVersion, LastPublishedUtc)
                VALUES (1, 'theme-neon-purple', 1, @Now);";
            metaIns.Parameters.AddWithValue("@Now", nowIso);
            await metaIns.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<List<ThemeConfig>> GetThemesAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await GetThemesInternalAsync(conn, ct);
    }

    private static async Task<List<ThemeConfig>> GetThemesInternalAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ConfigJson, IsDefault, IsPublished, Version, UpdatedAtUtc FROM Themes ORDER BY IsDefault DESC, Name ASC;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var items = new List<ThemeConfig>();
        while (await reader.ReadAsync(ct))
        {
            ThemeConfig? cfg = JsonSerializer.Deserialize<ThemeConfig>(reader.GetString(0));
            if (cfg is not null)
            {
                cfg.IsDefault = reader.GetInt32(1) == 1;
                cfg.IsPublished = reader.GetInt32(2) == 1;
                cfg.Version = reader.GetInt64(3);
                cfg.UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(4));
                items.Add(cfg);
            }
        }
        return items;
    }

    public async Task<ThemeConfig?> GetThemeByIdAsync(string id, CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await GetThemeByIdInternalAsync(conn, null, id, ct);
    }

    private static async Task<ThemeConfig?> GetThemeByIdInternalAsync(
        SqliteConnection conn,
        SqliteTransaction? tx,
        string id,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT ConfigJson, IsDefault, IsPublished, Version, UpdatedAtUtc FROM Themes WHERE Id = @Id LIMIT 1;";
        cmd.Parameters.AddWithValue("@Id", id);
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
        {
            return null;
        }

        ThemeConfig? cfg = JsonSerializer.Deserialize<ThemeConfig>(reader.GetString(0));
        if (cfg is not null)
        {
            cfg.IsDefault = reader.GetInt32(1) == 1;
            cfg.IsPublished = reader.GetInt32(2) == 1;
            cfg.Version = reader.GetInt64(3);
            cfg.UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(4));
        }
        return cfg;
    }

    public async Task<(string ActiveThemeId, long PublishedVersion, DateTimeOffset LastPublishedUtc)> GetThemeMetadataAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT ActiveThemeId, PublishedVersion, LastPublishedUtc FROM ThemeMetadata WHERE Id = 1 LIMIT 1;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (await reader.ReadAsync(ct))
        {
            return (reader.GetString(0), reader.GetInt64(1), DateTimeOffset.Parse(reader.GetString(2)));
        }
        return ("theme-neon-purple", 1, DateTimeOffset.UtcNow);
    }

    public async Task<ThemeConfig> SaveThemeDraftAsync(
        ThemeConfig theme,
        bool isNew,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            theme.UpdatedAtUtc = now;

            if (isNew)
            {
                if (string.IsNullOrWhiteSpace(theme.Id))
                {
                    theme.Id = $"theme-{Guid.NewGuid():N}"[..18];
                }
                theme.IsPublished = false;
                theme.Version = 1;
            }
            else
            {
                ThemeConfig? existing = await GetThemeByIdInternalAsync(conn, tx, theme.Id, ct);
                if (existing is null)
                {
                    throw new KeyNotFoundException($"Theme '{theme.Id}' not found.");
                }
                // Preserve publish and default flags on draft save; publishing happens explicitly via PublishThemeAsync
                theme.IsPublished = existing.IsPublished;
                theme.IsDefault = existing.IsDefault;
                theme.Version = existing.Version + 1;
            }

            string json = JsonSerializer.Serialize(theme);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = isNew
                ? @"INSERT INTO Themes (Id, Name, PresetKey, IsDefault, IsPublished, Version, ConfigJson, UpdatedAtUtc)
                    VALUES (@Id, @Name, @PresetKey, @IsDefault, 0, @Version, @ConfigJson, @UpdatedAtUtc);"
                : @"UPDATE Themes SET Name = @Name, PresetKey = @PresetKey, Version = @Version, ConfigJson = @ConfigJson, UpdatedAtUtc = @UpdatedAtUtc
                    WHERE Id = @Id;";

            cmd.Parameters.AddWithValue("@Id", theme.Id);
            cmd.Parameters.AddWithValue("@Name", theme.Name.Trim());
            cmd.Parameters.AddWithValue("@PresetKey", theme.PresetKey);
            cmd.Parameters.AddWithValue("@IsDefault", theme.IsDefault ? 1 : 0);
            cmd.Parameters.AddWithValue("@Version", theme.Version);
            cmd.Parameters.AddWithValue("@ConfigJson", json);
            cmd.Parameters.AddWithValue("@UpdatedAtUtc", now.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);

            await AppendThemeAuditLogInternalAsync(conn, tx, new ThemeAuditLog
            {
                Action = isNew ? "THEME_CREATED" : "THEME_DRAFT_SAVED",
                ThemeId = theme.Id,
                ThemeName = theme.Name,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = isNew
                    ? $"Created new theme '{theme.Name}' from preset {theme.PresetKey}."
                    : $"Saved draft changes to '{theme.Name}' (v{theme.Version})."
            }, ct);

            await tx.CommitAsync(ct);
            return theme;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ThemeConfig?> DuplicateThemeAsync(
        string sourceId,
        string newName,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            ThemeConfig? source = await GetThemeByIdInternalAsync(conn, tx, sourceId, ct);
            if (source is null)
            {
                return null;
            }

            ThemeConfig copy = source.Clone();
            copy.Id = $"theme-{Guid.NewGuid():N}"[..18];
            copy.Name = string.IsNullOrWhiteSpace(newName) ? $"{source.Name} Copy" : newName.Trim();
            copy.IsDefault = false;
            copy.IsPublished = false;
            copy.Version = 1;
            copy.UpdatedAtUtc = DateTimeOffset.UtcNow;

            string json = JsonSerializer.Serialize(copy);
            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = @"
                INSERT INTO Themes (Id, Name, PresetKey, IsDefault, IsPublished, Version, ConfigJson, UpdatedAtUtc)
                VALUES (@Id, @Name, @PresetKey, 0, 0, 1, @ConfigJson, @UpdatedAtUtc);";
            cmd.Parameters.AddWithValue("@Id", copy.Id);
            cmd.Parameters.AddWithValue("@Name", copy.Name);
            cmd.Parameters.AddWithValue("@PresetKey", copy.PresetKey);
            cmd.Parameters.AddWithValue("@ConfigJson", json);
            cmd.Parameters.AddWithValue("@UpdatedAtUtc", copy.UpdatedAtUtc.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);

            await AppendThemeAuditLogInternalAsync(conn, tx, new ThemeAuditLog
            {
                Action = "THEME_DUPLICATED",
                ThemeId = copy.Id,
                ThemeName = copy.Name,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Duplicated '{source.Name}' into '{copy.Name}'."
            }, ct);

            await tx.CommitAsync(ct);
            return copy;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ThemeConfig?> RenameThemeAsync(
        string id,
        string newName,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            ThemeConfig? existing = await GetThemeByIdInternalAsync(conn, tx, id, ct);
            if (existing is null)
            {
                return null;
            }

            string previousName = existing.Name;
            existing.Name = newName.Trim();
            existing.Version += 1;
            existing.UpdatedAtUtc = DateTimeOffset.UtcNow;

            await using var cmd = conn.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = "UPDATE Themes SET Name = @Name, Version = @Version, ConfigJson = @ConfigJson, UpdatedAtUtc = @UpdatedAtUtc WHERE Id = @Id;";
            cmd.Parameters.AddWithValue("@Id", id);
            cmd.Parameters.AddWithValue("@Name", existing.Name);
            cmd.Parameters.AddWithValue("@Version", existing.Version);
            cmd.Parameters.AddWithValue("@ConfigJson", JsonSerializer.Serialize(existing));
            cmd.Parameters.AddWithValue("@UpdatedAtUtc", existing.UpdatedAtUtc.ToString("O"));
            await cmd.ExecuteNonQueryAsync(ct);

            await AppendThemeAuditLogInternalAsync(conn, tx, new ThemeAuditLog
            {
                Action = "THEME_RENAMED",
                ThemeId = existing.Id,
                ThemeName = existing.Name,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Renamed theme '{previousName}' to '{existing.Name}'."
            }, ct);

            await tx.CommitAsync(ct);
            return existing;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<(bool Deleted, string Error)> DeleteThemeSafelyAsync(
        string id,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            ThemeConfig? existing = await GetThemeByIdInternalAsync(conn, tx, id, ct);
            if (existing is null)
            {
                return (false, "Theme not found.");
            }
            if (existing.IsDefault)
            {
                return (false, "Cannot delete the default theme. Set another theme as default first.");
            }
            if (existing.IsPublished)
            {
                return (false, "Cannot delete the currently published client theme. Publish another theme first.");
            }

            await using var delCmd = conn.CreateCommand();
            delCmd.Transaction = tx;
            delCmd.CommandText = "DELETE FROM Themes WHERE Id = @Id AND IsDefault = 0 AND IsPublished = 0;";
            delCmd.Parameters.AddWithValue("@Id", id);
            await delCmd.ExecuteNonQueryAsync(ct);

            await AppendThemeAuditLogInternalAsync(conn, tx, new ThemeAuditLog
            {
                Action = "THEME_DELETED",
                ThemeId = existing.Id,
                ThemeName = existing.Name,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Deleted unused theme '{existing.Name}'."
            }, ct);

            await tx.CommitAsync(ct);
            return (true, string.Empty);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<ThemeConfig?> SetDefaultThemeAsync(
        string id,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            ThemeConfig? target = await GetThemeByIdInternalAsync(conn, tx, id, ct);
            if (target is null)
            {
                return null;
            }

            List<ThemeConfig> all = await GetThemesInternalAsync(conn, ct);
            string nowIso = DateTimeOffset.UtcNow.ToString("O");
            foreach (ThemeConfig item in all)
            {
                item.IsDefault = string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase);
                item.UpdatedAtUtc = DateTimeOffset.UtcNow;
                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE Themes SET IsDefault = @IsDefault, ConfigJson = @Json, UpdatedAtUtc = @Now WHERE Id = @Id;";
                upd.Parameters.AddWithValue("@Id", item.Id);
                upd.Parameters.AddWithValue("@IsDefault", item.IsDefault ? 1 : 0);
                upd.Parameters.AddWithValue("@Json", JsonSerializer.Serialize(item));
                upd.Parameters.AddWithValue("@Now", nowIso);
                await upd.ExecuteNonQueryAsync(ct);
            }

            target.IsDefault = true;
            await AppendThemeAuditLogInternalAsync(conn, tx, new ThemeAuditLog
            {
                Action = "THEME_SET_DEFAULT",
                ThemeId = target.Id,
                ThemeName = target.Name,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Marked '{target.Name}' as default theme."
            }, ct);

            await tx.CommitAsync(ct);
            return target;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PublishedThemePayload?> PublishThemeAsync(
        string id,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            ThemeConfig? target = await GetThemeByIdInternalAsync(conn, tx, id, ct);
            if (target is null)
            {
                return null;
            }

            List<ThemeConfig> all = await GetThemesInternalAsync(conn, ct);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            string nowIso = now.ToString("O");

            foreach (ThemeConfig item in all)
            {
                bool active = string.Equals(item.Id, id, StringComparison.OrdinalIgnoreCase);
                item.IsPublished = active;
                if (active)
                {
                    item.Version += 1;
                    item.UpdatedAtUtc = now;
                    target = item;
                }

                await using var upd = conn.CreateCommand();
                upd.Transaction = tx;
                upd.CommandText = "UPDATE Themes SET IsPublished = @Pub, Version = @Ver, ConfigJson = @Json, UpdatedAtUtc = @Now WHERE Id = @Id;";
                upd.Parameters.AddWithValue("@Id", item.Id);
                upd.Parameters.AddWithValue("@Pub", item.IsPublished ? 1 : 0);
                upd.Parameters.AddWithValue("@Ver", item.Version);
                upd.Parameters.AddWithValue("@Json", JsonSerializer.Serialize(item));
                upd.Parameters.AddWithValue("@Now", nowIso);
                await upd.ExecuteNonQueryAsync(ct);
            }

            long nextPublishedVersion = 1;
            await using (var verCmd = conn.CreateCommand())
            {
                verCmd.Transaction = tx;
                verCmd.CommandText = "SELECT PublishedVersion FROM ThemeMetadata WHERE Id = 1 LIMIT 1;";
                object? scalar = await verCmd.ExecuteScalarAsync(ct);
                if (scalar is not null && scalar != DBNull.Value)
                {
                    nextPublishedVersion = Convert.ToInt64(scalar) + 1;
                }
            }

            await using (var metaUpd = conn.CreateCommand())
            {
                metaUpd.Transaction = tx;
                metaUpd.CommandText = @"
                    INSERT INTO ThemeMetadata (Id, ActiveThemeId, PublishedVersion, LastPublishedUtc)
                    VALUES (1, @ActiveId, @Ver, @Now)
                    ON CONFLICT(Id) DO UPDATE SET
                        ActiveThemeId = excluded.ActiveThemeId,
                        PublishedVersion = excluded.PublishedVersion,
                        LastPublishedUtc = excluded.LastPublishedUtc;";
                metaUpd.Parameters.AddWithValue("@ActiveId", target.Id);
                metaUpd.Parameters.AddWithValue("@Ver", nextPublishedVersion);
                metaUpd.Parameters.AddWithValue("@Now", nowIso);
                await metaUpd.ExecuteNonQueryAsync(ct);
            }

            await IncrementCatalogVersionAsync(conn, tx, ct);

            await AppendThemeAuditLogInternalAsync(conn, tx, new ThemeAuditLog
            {
                Action = "THEME_PUBLISHED",
                ThemeId = target.Id,
                ThemeName = target.Name,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Published '{target.Name}' to CCBoot clients (v{nextPublishedVersion}). App name: {target.ApplicationNameEn} / {target.ApplicationNameAr}."
            }, ct);

            await tx.CommitAsync(ct);

            List<ThemeAssetMetadata> assets = await GetThemeAssetsAsync(ct);
            return new PublishedThemePayload
            {
                PublishedVersion = nextPublishedVersion,
                PublishedAtUtc = now,
                Theme = target,
                Assets = assets
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PublishedThemePayload> RestoreDefaultThemeAsync(
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        ThemeConfig factoryDefault = ThemePresetFactory.CreateNeonPurple(isDefault: true, isPublished: true);
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            DateTimeOffset now = DateTimeOffset.UtcNow;
            string nowIso = now.ToString("O");
            ThemeConfig? existingDefault = await GetThemeByIdInternalAsync(conn, tx, factoryDefault.Id, ct);
            factoryDefault.Version = (existingDefault?.Version ?? 0) + 1;
            factoryDefault.UpdatedAtUtc = now;

            await using (var resetAll = conn.CreateCommand())
            {
                resetAll.Transaction = tx;
                resetAll.CommandText = "UPDATE Themes SET IsPublished = 0, IsDefault = 0;";
                await resetAll.ExecuteNonQueryAsync(ct);
            }

            await using (var upsertDef = conn.CreateCommand())
            {
                upsertDef.Transaction = tx;
                upsertDef.CommandText = @"
                    INSERT INTO Themes (Id, Name, PresetKey, IsDefault, IsPublished, Version, ConfigJson, UpdatedAtUtc)
                    VALUES (@Id, @Name, @PresetKey, 1, 1, @Version, @Json, @Now)
                    ON CONFLICT(Id) DO UPDATE SET
                        Name = excluded.Name,
                        PresetKey = excluded.PresetKey,
                        IsDefault = 1,
                        IsPublished = 1,
                        Version = excluded.Version,
                        ConfigJson = excluded.ConfigJson,
                        UpdatedAtUtc = excluded.UpdatedAtUtc;";
                upsertDef.Parameters.AddWithValue("@Id", factoryDefault.Id);
                upsertDef.Parameters.AddWithValue("@Name", factoryDefault.Name);
                upsertDef.Parameters.AddWithValue("@PresetKey", factoryDefault.PresetKey);
                upsertDef.Parameters.AddWithValue("@Version", factoryDefault.Version);
                upsertDef.Parameters.AddWithValue("@Json", JsonSerializer.Serialize(factoryDefault));
                upsertDef.Parameters.AddWithValue("@Now", nowIso);
                await upsertDef.ExecuteNonQueryAsync(ct);
            }

            long nextPublishedVersion = 1;
            await using (var verCmd = conn.CreateCommand())
            {
                verCmd.Transaction = tx;
                verCmd.CommandText = "SELECT PublishedVersion FROM ThemeMetadata WHERE Id = 1 LIMIT 1;";
                object? scalar = await verCmd.ExecuteScalarAsync(ct);
                if (scalar is not null && scalar != DBNull.Value)
                {
                    nextPublishedVersion = Convert.ToInt64(scalar) + 1;
                }
            }

            await using (var metaUpd = conn.CreateCommand())
            {
                metaUpd.Transaction = tx;
                metaUpd.CommandText = @"
                    INSERT INTO ThemeMetadata (Id, ActiveThemeId, PublishedVersion, LastPublishedUtc)
                    VALUES (1, @ActiveId, @Ver, @Now)
                    ON CONFLICT(Id) DO UPDATE SET
                        ActiveThemeId = excluded.ActiveThemeId,
                        PublishedVersion = excluded.PublishedVersion,
                        LastPublishedUtc = excluded.LastPublishedUtc;";
                metaUpd.Parameters.AddWithValue("@ActiveId", factoryDefault.Id);
                metaUpd.Parameters.AddWithValue("@Ver", nextPublishedVersion);
                metaUpd.Parameters.AddWithValue("@Now", nowIso);
                await metaUpd.ExecuteNonQueryAsync(ct);
            }

            await IncrementCatalogVersionAsync(conn, tx, ct);

            await AppendThemeAuditLogInternalAsync(conn, tx, new ThemeAuditLog
            {
                Action = "THEME_RESTORED_DEFAULT",
                ThemeId = factoryDefault.Id,
                ThemeName = factoryDefault.Name,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Restored original CYBER HUB default theme and branding (v{nextPublishedVersion})."
            }, ct);

            await tx.CommitAsync(ct);
            return new PublishedThemePayload
            {
                PublishedVersion = nextPublishedVersion,
                PublishedAtUtc = now,
                Theme = factoryDefault,
                Assets = await GetThemeAssetsAsync(ct)
            };
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<PublishedThemePayload> GetPublishedThemePayloadAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        string activeId = "theme-neon-purple";
        long publishedVer = 1;
        DateTimeOffset lastPub = DateTimeOffset.UtcNow;

        await using (var metaCmd = conn.CreateCommand())
        {
            metaCmd.CommandText = "SELECT ActiveThemeId, PublishedVersion, LastPublishedUtc FROM ThemeMetadata WHERE Id = 1 LIMIT 1;";
            await using var r = await metaCmd.ExecuteReaderAsync(ct);
            if (await r.ReadAsync(ct))
            {
                activeId = r.GetString(0);
                publishedVer = r.GetInt64(1);
                lastPub = DateTimeOffset.Parse(r.GetString(2));
            }
        }

        ThemeConfig? active = await GetThemeByIdInternalAsync(conn, null, activeId, ct)
            ?? ThemePresetFactory.CreateNeonPurple(isDefault: true, isPublished: true);

        return new PublishedThemePayload
        {
            PublishedVersion = publishedVer,
            PublishedAtUtc = lastPub,
            Theme = active,
            Assets = await GetThemeAssetsInternalAsync(conn, ct)
        };
    }

    public async Task RecordThemeAssetAsync(
        ThemeAssetMetadata asset,
        string workstation,
        string sourceIp,
        CancellationToken ct = default)
    {
        await _writeLock.WaitAsync(ct);
        try
        {
            await using var conn = new SqliteConnection(_connectionString);
            await conn.OpenAsync(ct);
            await using var tx = (SqliteTransaction)await conn.BeginTransactionAsync(ct);

            await using (var cmd = conn.CreateCommand())
            {
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    INSERT INTO ThemeAssets (Id, FileName, OriginalFileName, MediaKind, ContentType, SizeBytes, RelativeUrl, UploadedAtUtc)
                    VALUES (@Id, @FileName, @OriginalFileName, @MediaKind, @ContentType, @SizeBytes, @RelativeUrl, @UploadedAtUtc);";
                cmd.Parameters.AddWithValue("@Id", asset.Id);
                cmd.Parameters.AddWithValue("@FileName", asset.FileName);
                cmd.Parameters.AddWithValue("@OriginalFileName", asset.OriginalFileName);
                cmd.Parameters.AddWithValue("@MediaKind", asset.MediaKind);
                cmd.Parameters.AddWithValue("@ContentType", asset.ContentType);
                cmd.Parameters.AddWithValue("@SizeBytes", asset.SizeBytes);
                cmd.Parameters.AddWithValue("@RelativeUrl", asset.RelativeUrl);
                cmd.Parameters.AddWithValue("@UploadedAtUtc", asset.UploadedAtUtc.ToString("O"));
                await cmd.ExecuteNonQueryAsync(ct);
            }

            await AppendThemeAuditLogInternalAsync(conn, tx, new ThemeAuditLog
            {
                Action = "THEME_ASSET_UPLOADED",
                ThemeId = asset.Id,
                ThemeName = asset.OriginalFileName,
                WorkstationName = workstation,
                SourceIp = sourceIp,
                Details = $"Uploaded {asset.MediaKind} asset {asset.FileName} ({asset.SizeBytes} bytes)."
            }, ct);

            await tx.CommitAsync(ct);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public async Task<List<ThemeAssetMetadata>> GetThemeAssetsAsync(CancellationToken ct = default)
    {
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);
        return await GetThemeAssetsInternalAsync(conn, ct);
    }

    private static async Task<List<ThemeAssetMetadata>> GetThemeAssetsInternalAsync(SqliteConnection conn, CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, FileName, OriginalFileName, MediaKind, ContentType, SizeBytes, RelativeUrl, UploadedAtUtc FROM ThemeAssets ORDER BY UploadedAtUtc DESC LIMIT 100;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var list = new List<ThemeAssetMetadata>();
        while (await reader.ReadAsync(ct))
        {
            list.Add(new ThemeAssetMetadata
            {
                Id = reader.GetString(0),
                FileName = reader.GetString(1),
                OriginalFileName = reader.GetString(2),
                MediaKind = reader.GetString(3),
                ContentType = reader.GetString(4),
                SizeBytes = reader.GetInt64(5),
                RelativeUrl = reader.GetString(6),
                UploadedAtUtc = DateTimeOffset.Parse(reader.GetString(7))
            });
        }
        return list;
    }

    public async Task<List<ThemeAuditLog>> GetThemeAuditLogsAsync(int limit = 200, CancellationToken ct = default)
    {
        int safeLimit = Math.Clamp(limit, 1, 500);
        await using var conn = new SqliteConnection(_connectionString);
        await conn.OpenAsync(ct);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT Id, TimestampUtc, Action, ThemeId, ThemeName, WorkstationName, SourceIp, Details FROM ThemeAuditLogs ORDER BY Id DESC LIMIT @Limit;";
        cmd.Parameters.AddWithValue("@Limit", safeLimit);
        await using var reader = await cmd.ExecuteReaderAsync(ct);

        var logs = new List<ThemeAuditLog>();
        while (await reader.ReadAsync(ct))
        {
            logs.Add(new ThemeAuditLog
            {
                Id = reader.GetInt64(0),
                TimestampUtc = DateTimeOffset.Parse(reader.GetString(1)),
                Action = reader.GetString(2),
                ThemeId = reader.GetString(3),
                ThemeName = reader.GetString(4),
                WorkstationName = reader.GetString(5),
                SourceIp = reader.GetString(6),
                Details = reader.GetString(7)
            });
        }
        return logs;
    }

    private static async Task AppendAuditLogInternalAsync(
        SqliteConnection conn,
        SqliteTransaction? tx,
        AdminAuditLog entry,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
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

    private static async Task AppendThemeAuditLogInternalAsync(
        SqliteConnection conn,
        SqliteTransaction? tx,
        ThemeAuditLog entry,
        CancellationToken ct)
    {
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO ThemeAuditLogs (TimestampUtc, Action, ThemeId, ThemeName, WorkstationName, SourceIp, Details)
            VALUES (@Ts, @Action, @ThemeId, @ThemeName, @Workstation, @Ip, @Details);";
        cmd.Parameters.AddWithValue("@Ts", entry.TimestampUtc.ToString("O"));
        cmd.Parameters.AddWithValue("@Action", entry.Action);
        cmd.Parameters.AddWithValue("@ThemeId", entry.ThemeId);
        cmd.Parameters.AddWithValue("@ThemeName", entry.ThemeName);
        cmd.Parameters.AddWithValue("@Workstation", entry.WorkstationName);
        cmd.Parameters.AddWithValue("@Ip", entry.SourceIp);
        cmd.Parameters.AddWithValue("@Details", entry.Details);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    private static async Task<Dictionary<string, List<string>>> LoadGameCategoryIdsAsync(SqliteConnection conn, CancellationToken ct)
    {
        var map = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT GameId, CategoryId FROM GameCategories;";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            string gameId = reader.GetString(0);
            string catId = reader.GetString(1);
            if (!map.TryGetValue(gameId, out List<string>? list))
            {
                list = new List<string>();
                map[gameId] = list;
            }
            list.Add(catId);
        }
        return map;
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
        NetworkMode = (NetworkMode)reader.GetInt32(3),
        Description = reader.GetString(4),
        Developer = reader.GetString(5),
        CoverImagePath = reader.GetString(6),
        HeroImagePath = reader.GetString(7),
        StorageLocationId = reader.GetString(8),
        ExecutablePath = reader.GetString(9),
        LaunchArguments = reader.GetString(10),
        LaunchType = (LaunchType)reader.GetInt32(11),
        ExternalAppId = reader.GetString(12),
        ProcessNameToMonitor = reader.GetString(13),
        IsVisible = reader.GetInt32(14) == 1,
        IsFeatured = reader.GetInt32(15) == 1,
        SortOrder = reader.GetInt32(16),
        CreatedAtUtc = DateTimeOffset.Parse(reader.GetString(17)),
        UpdatedAtUtc = DateTimeOffset.Parse(reader.GetString(18))
    };
}
