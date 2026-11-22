using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.ServerService.Security;
using CyberHub.ServerService.Services;
using CyberHub.Shared.Security;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "CyberHubServerService";
});

builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 55L * 1024L * 1024L;
});

ServerOptions initialOptions = builder.Configuration
    .GetSection(ServerOptions.SectionName)
    .Get<ServerOptions>() ?? new ServerOptions();

string? envSecret = Environment.GetEnvironmentVariable("CYBERHUB_TOKEN_SECRET");
string effectiveSecret = envSecret ?? initialOptions.TokenSigningSecret;
builder.Services.AddSingleton(new TokenService(effectiveSecret));
builder.Services.AddSingleton(new RecoveryCodeService(effectiveSecret));
builder.Services.AddSingleton<SqliteGameRepository>();
builder.Services.AddSingleton<CatalogSyncService>();
builder.Services.AddHttpClient<CoverArtLookupService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.DefaultRequestHeaders.UserAgent.ParseAdd("CyberHubServer/1.0");
});
builder.Services.AddSingleton<GameScannerService>();
builder.Services.AddControllers();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<SqliteGameRepository>();
    await repo.InitializeAsync();
    long epoch = await repo.GetTokenEpochAsync();
    var tokenService = scope.ServiceProvider.GetRequiredService<TokenService>();
    tokenService.SetMinimumValidEpoch(epoch);
    var sync = scope.ServiceProvider.GetRequiredService<CatalogSyncService>();
    await sync.PublishSnapshotToShareAsync();
}

Directory.CreateDirectory(initialOptions.CoversDirectory);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(initialOptions.CoversDirectory)),
    RequestPath = "/covers"
});

Directory.CreateDirectory(initialOptions.ThemeAssetsDirectory);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(initialOptions.ThemeAssetsDirectory)),
    RequestPath = "/theme-assets"
});

app.UseMiddleware<AdminWorkstationMiddleware>();
app.MapControllers();

app.Run();
