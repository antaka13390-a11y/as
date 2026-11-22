using System.Text.Json;
using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.ServerService.Security;
using CyberHub.ServerService.Services;
using CyberHub.Shared.Localization;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace CyberHub.Tests;

public sealed class ThemeAndAccountTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly ServerOptions _options;
    private readonly SqliteGameRepository _repository;
    private readonly TokenService _tokenService;

    public ThemeAndAccountTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), $"cyberhub-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempRoot);

        _options = new ServerOptions
        {
            DatabasePath = Path.Combine(_tempRoot, "cyberhub.db"),
            BackupDirectory = Path.Combine(_tempRoot, "backups"),
            PublishedCatalogPath = Path.Combine(_tempRoot, "published", "catalog.json"),
            CoversDirectory = Path.Combine(_tempRoot, "published", "covers"),
            PublishedThemePath = Path.Combine(_tempRoot, "published", "theme.json"),
            ThemeAssetsDirectory = Path.Combine(_tempRoot, "published", "theme-assets"),
            AllowedAdminIps = new[] { "127.0.0.1", "192.168.10.50" },
            AllowedAdminWorkstations = new[] { "ADMIN-PC" }
        };

        _repository = new SqliteGameRepository(Options.Create(_options));
        _tokenService = new TokenService("0123456789abcdef0123456789abcdef0123456789abcdef");
    }

    [Fact]
    public async Task PasswordChange_VerifiesCurrentPassword_RevokesExistingTokens_AndPreservesHistory()
    {
        await _repository.InitializeAsync();
        string initialHash = PasswordHasher.HashPassword("InitialStrong#2026");
        Assert.True(await _repository.SetAdminPasswordHashIfUnsetAsync(initialHash));

        string oldToken = _tokenService.IssueToken("CyberHubAdmin", "ADMIN-PC", "192.168.10.50", TimeSpan.FromHours(4));
        Assert.True(_tokenService.ValidateToken(oldToken, "192.168.10.50", out _));

        (bool wrongAttemptOk, _) = await _repository.ChangeAdminPasswordAsync(
            "WrongPassword#123",
            "UpdatedStrong#2026",
            _tokenService,
            "ADMIN-PC",
            "192.168.10.50");
        Assert.False(wrongAttemptOk);
        Assert.True(_tokenService.ValidateToken(oldToken, "192.168.10.50", out _));

        (bool changedOk, string err) = await _repository.ChangeAdminPasswordAsync(
            "InitialStrong#2026",
            "UpdatedStrong#2026",
            _tokenService,
            "ADMIN-PC",
            "192.168.10.50");
        Assert.True(changedOk, err);

        // Existing token must now be rejected due to bumped TokenEpoch
        Assert.False(_tokenService.ValidateToken(oldToken, "192.168.10.50", out _));

        string newToken = _tokenService.IssueToken("CyberHubAdmin", "ADMIN-PC", "192.168.10.50", TimeSpan.FromHours(4));
        Assert.True(_tokenService.ValidateToken(newToken, "192.168.10.50", out _));

        List<AdminAuditLog> logs = await _repository.GetAuditLogsAsync(20);
        AdminAuditLog? pwdLog = logs.FirstOrDefault(l => l.Action == "ADMIN_PASSWORD_CHANGED");
        Assert.NotNull(pwdLog);
        Assert.DoesNotContain("UpdatedStrong#2026", pwdLog!.Details, StringComparison.Ordinal);
        Assert.DoesNotContain("InitialStrong#2026", pwdLog.Details, StringComparison.Ordinal);
    }

    [Fact]
    public void AdminWorkstationMiddleware_BlocksUnauthorizedCustomerIpForThemeEdits()
    {
        var middleware = new AdminWorkstationMiddleware(
            _ => Task.CompletedTask,
            _tokenService,
            Options.Create(_options),
            NullLogger<AdminWorkstationMiddleware>.Instance);

        Assert.True(middleware.IsWorkstationAuthorized("192.168.10.50", "ADMIN-PC"));
        Assert.False(middleware.IsWorkstationAuthorized("192.168.10.105", "PC-01"));
    }

    [Fact]
    public async Task ThemeCrud_DuplicateDeleteProtection_AndPublishSyncWorkEndToEnd()
    {
        await _repository.InitializeAsync();
        List<ThemeConfig> initial = await _repository.GetThemesAsync();
        Assert.True(initial.Count >= 6);

        ThemeConfig draft = new()
        {
            Name = "Arena Crimson",
            PresetKey = "Custom",
            PrimaryAccentColor = "#EF4444",
            SecondaryAccentColor = "#F59E0B",
            ApplicationNameEn = "ARENA OPS",
            ApplicationNameAr = "أرينا أوبس"
        };

        ThemeConfig created = await _repository.SaveThemeDraftAsync(draft, isNew: true, "ADMIN-PC", "192.168.10.50");
        Assert.False(created.IsPublished);

        // Editing/creating a draft must NOT alter published theme yet
        PublishedThemePayload beforePublish = await _repository.GetPublishedThemePayloadAsync();
        Assert.Equal("CYBER HUB", beforePublish.Theme.ApplicationNameEn);

        ThemeConfig? dup = await _repository.DuplicateThemeAsync(created.Id, "Arena Crimson V2", "ADMIN-PC", "192.168.10.50");
        Assert.NotNull(dup);

        PublishedThemePayload? published = await _repository.PublishThemeAsync(dup!.Id, "ADMIN-PC", "192.168.10.50");
        Assert.NotNull(published);
        Assert.Equal("ARENA OPS", published!.Theme.ApplicationNameEn);
        Assert.Equal("أرينا أوبس", published.Theme.ApplicationNameAr);

        // Deleting currently published theme is safely blocked
        (bool deletedPublished, _) = await _repository.DeleteThemeSafelyAsync(dup.Id, "ADMIN-PC", "192.168.10.50");
        Assert.False(deletedPublished);

        // Sync snapshot writes theme.json for offline SMB client fallback
        var sync = new CatalogSyncService(_repository, Options.Create(_options), NullLogger<CatalogSyncService>.Instance);
        await sync.PublishSnapshotToShareAsync();
        Assert.True(File.Exists(_options.PublishedThemePath));

        string themeJson = await File.ReadAllTextAsync(_options.PublishedThemePath);
        PublishedThemePayload? deserialized = JsonSerializer.Deserialize<PublishedThemePayload>(themeJson);
        Assert.NotNull(deserialized);
        Assert.Equal("ARENA OPS", deserialized!.Theme.ApplicationNameEn);
    }

    [Fact]
    public void AssetUploadValidator_RejectsSpoofedExtensions_Executables_AndPathTraversal()
    {
        byte[] exeHeader = { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00 };
        AssetValidationResult spoofedPng = AssetUploadValidator.ValidateAsset("evil.png", exeHeader, exeHeader.Length);
        Assert.False(spoofedPng.IsValid);

        AssetValidationResult traversal = AssetUploadValidator.ValidateAsset("..\\..\\evil.jpg", new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }, 1024);
        Assert.False(traversal.IsValid);

        byte[] validPng = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00 };
        AssetValidationResult okPng = AssetUploadValidator.ValidateAsset("logo.png", validPng, 1024);
        Assert.True(okPng.IsValid);
        Assert.Equal("image", okPng.MediaKind);
    }

    [Fact]
    public void LocalizationCatalog_SupportsArabicAndEnglishForThemeAndScannerKeys()
    {
        Assert.Equal("المظهر والثيمات", LocalizationCatalog.Get("ar", "Admin.TabThemes"));
        Assert.Equal("Appearance & Themes", LocalizationCatalog.Get("en", "Admin.TabThemes"));
        Assert.Equal("البحث عن الألعاب", LocalizationCatalog.Get("ar", "Scan.TabTitle"));
        Assert.Equal("Scan for Games", LocalizationCatalog.Get("en", "Scan.TabTitle"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
        {
            Directory.Delete(_tempRoot, recursive: true);
        }
    }
}
