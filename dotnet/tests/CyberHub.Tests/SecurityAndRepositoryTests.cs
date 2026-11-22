using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using Microsoft.Extensions.Options;
using Xunit;

namespace CyberHub.Tests;

public sealed class SecurityAndRepositoryTests : IDisposable
{
    private readonly string _tempDir;
    private readonly SqliteGameRepository _repository;

    public SecurityAndRepositoryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "cyberhub-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        var options = Options.Create(new ServerOptions
        {
            DatabasePath = Path.Combine(_tempDir, "cyberhub-test.db"),
            BackupDirectory = Path.Combine(_tempDir, "backups"),
            PublishedCatalogPath = Path.Combine(_tempDir, "catalog.json"),
            CoversDirectory = Path.Combine(_tempDir, "covers")
        });

        _repository = new SqliteGameRepository(options);
    }

    [Fact]
    public void PasswordHasher_HashesAndVerifiesWithConstantTime()
    {
        string hash = PasswordHasher.HashPassword("CyberCafe#Admin2026!");
        Assert.True(PasswordHasher.VerifyPassword("CyberCafe#Admin2026!", hash));
        Assert.False(PasswordHasher.VerifyPassword("WrongPassword", hash));
    }

    [Fact]
    public void UncPathValidator_RejectsShellInjectionAndTraversal()
    {
        (bool validInjection, _) = UncPathValidator.ValidateGamePath(
            LaunchType.DirectExecutable,
            @"\\CCBOOT-SRV\Games\Game.exe",
            string.Empty,
            "-fullscreen & calc.exe");
        Assert.False(validInjection);

        (bool validTraversal, _) = UncPathValidator.ValidateGamePath(
            LaunchType.DirectExecutable,
            @"\\CCBOOT-SRV\Games\..\Windows\System32\cmd.exe",
            string.Empty,
            string.Empty);
        Assert.False(validTraversal);

        (bool validUnc, _) = UncPathValidator.ValidateGamePath(
            LaunchType.DirectExecutable,
            @"\\CCBOOT-SRV\Games\CCBoot\CyberGame\game.exe",
            string.Empty,
            "-novid -high");
        Assert.True(validUnc);
    }

    [Fact]
    public void TokenService_EnforcesSourceIpAndExpiry()
    {
        var tokenService = new TokenService("0123456789ABCDEF0123456789ABCDEF");
        string token = tokenService.IssueToken("CyberHubAdmin", "ADMIN-PC", "192.168.10.50", TimeSpan.FromMinutes(15));

        Assert.True(tokenService.ValidateToken(token, "192.168.10.50", out TokenPayload? payload));
        Assert.NotNull(payload);
        Assert.False(tokenService.ValidateToken(token, "192.168.10.105", out _));
    }

    [Fact]
    public async Task Categories_CreateAndDeleteSafelyWithoutDeletingGames()
    {
        await _repository.InitializeAsync();

        List<CategoryEntry> seeded = await _repository.GetCategoriesAsync();
        Assert.Contains(seeded, c => c.NameEn == "All Games" && c.IsSystem);
        Assert.Contains(seeded, c => c.NameEn == "Online Games" && c.IsSystem);
        Assert.Contains(seeded, c => c.NameEn == "Offline Games" && c.IsSystem);

        var custom = new CategoryEntry
        {
            Id = "cat-esports",
            Slug = "esports-arena",
            NameEn = "Esports Arena",
            NameAr = "ساحة الرياضات الإلكترونية",
            IconKey = "trophy",
            DisplayOrder = 12
        };
        await _repository.UpsertCategoryAsync(custom, isNew: true);

        var game = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "Arena Clash",
            Category = "Esports Arena",
            CategoryIds = new List<string> { "cat-esports", "cat-action" },
            NetworkMode = NetworkMode.Online,
            ExecutablePath = @"\\CCBOOT-SRV\Games\Arena\arena.exe",
            LaunchType = LaunchType.DirectExecutable
        };
        await _repository.UpsertGameAsync(game, isNew: true);

        // System categories cannot be deleted
        (bool systemDeleted, _) = await _repository.DeleteCategorySafelyAsync("cat-all");
        Assert.False(systemDeleted);

        // Deleting custom category removes the link only and preserves the game record
        (bool customDeleted, _) = await _repository.DeleteCategorySafelyAsync("cat-esports");
        Assert.True(customDeleted);

        GameEntry? persistedGame = await _repository.GetGameByIdAsync(game.Id);
        Assert.NotNull(persistedGame);
        Assert.DoesNotContain("cat-esports", persistedGame!.CategoryIds);
        Assert.Contains("cat-action", persistedGame.CategoryIds);
    }

    [Fact]
    public async Task Games_SupportMultipleCategoriesAndOnlineOfflineClassification()
    {
        await _repository.InitializeAsync();

        var offlineRpg = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "Shadow Keep",
            Category = "Action",
            CategoryIds = new List<string> { "cat-action", "cat-adventure" },
            NetworkMode = NetworkMode.Offline,
            ExecutablePath = @"\\CCBOOT-SRV\Games\ShadowKeep\shadow.exe",
            LaunchType = LaunchType.DirectExecutable,
            IsVisible = true
        };

        await _repository.UpsertGameAsync(offlineRpg, isNew: true);
        GameEntry? loaded = await _repository.GetGameByIdAsync(offlineRpg.Id);

        Assert.NotNull(loaded);
        Assert.Equal(NetworkMode.Offline, loaded!.NetworkMode);
        Assert.Equal(2, loaded.CategoryIds.Count);
        Assert.Contains("cat-action", loaded.CategoryIds);
        Assert.Contains("cat-adventure", loaded.CategoryIds);
    }

    [Fact]
    public async Task DeleteGameRecordOnly_NeverDeletesTargetGameFileOnDisk()
    {
        await _repository.InitializeAsync();

        string simulatedGameExe = Path.Combine(_tempDir, "FakeGame.exe");
        await File.WriteAllTextAsync(simulatedGameExe, "binary-placeholder");

        var game = new GameEntry
        {
            Id = Guid.NewGuid(),
            Name = "Test Title",
            Category = "Action",
            CategoryIds = new List<string> { "cat-action" },
            NetworkMode = NetworkMode.Offline,
            ExecutablePath = simulatedGameExe,
            LaunchType = LaunchType.DirectExecutable,
            IsVisible = true
        };

        await _repository.UpsertGameAsync(game, isNew: true);
        bool removed = await _repository.DeleteGameRecordOnlyAsync(game.Id);

        Assert.True(removed);
        Assert.Null(await _repository.GetGameByIdAsync(game.Id));
        Assert.True(File.Exists(simulatedGameExe));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }
}
