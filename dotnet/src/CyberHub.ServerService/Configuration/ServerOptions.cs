namespace CyberHub.ServerService.Configuration;

public sealed class ServerOptions
{
    public const string SectionName = "CyberHubServer";

    public string DatabasePath { get; set; } = @"C:\ProgramData\CyberHub\Data\cyberhub.db";
    public string BackupDirectory { get; set; } = @"C:\ProgramData\CyberHub\Backups";
    public string PublishedCatalogPath { get; set; } = @"C:\CyberHubShares\PublishedCatalog\catalog.json";
    public string CoversDirectory { get; set; } = @"C:\CyberHubShares\PublishedCatalog\covers";
    public string PublishedThemePath { get; set; } = @"C:\CyberHubShares\PublishedCatalog\theme.json";
    public string ThemeAssetsDirectory { get; set; } = @"C:\CyberHubShares\PublishedCatalog\theme-assets";
    public string[] AllowedAdminIps { get; set; } = new[] { "127.0.0.1", "::1", "192.168.10.50" };
    public string[] AllowedAdminWorkstations { get; set; } = new[] { "ADMIN-PC", "CCBOOT-SRV" };
    public string TokenSigningSecret { get; set; } = string.Empty;
    public int TokenLifetimeHours { get; set; } = 8;
    public int MaxCoverUploadBytes { get; set; } = 5 * 1024 * 1024;
    public int MaxImageUploadBytes { get; set; } = 10 * 1024 * 1024;
    public int MaxVideoUploadBytes { get; set; } = 50 * 1024 * 1024;
}
