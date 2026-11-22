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
