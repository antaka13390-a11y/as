using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.ServerService.Security;
using CyberHub.ServerService.Services;
using CyberHub.Shared.Security;
using Microsoft.Extensions.FileProviders;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseWindowsService(options =>
{
    options.ServiceName = "CyberHubServerService";
});

builder.Services.Configure<ServerOptions>(builder.Configuration.GetSection(ServerOptions.SectionName));

ServerOptions initialOptions = builder.Configuration
    .GetSection(ServerOptions.SectionName)
    .Get<ServerOptions>() ?? new ServerOptions();

string? envSecret = Environment.GetEnvironmentVariable("CYBERHUB_TOKEN_SECRET");
builder.Services.AddSingleton(new TokenService(envSecret ?? initialOptions.TokenSigningSecret));
builder.Services.AddSingleton<SqliteGameRepository>();
builder.Services.AddSingleton<CatalogSyncService>();
builder.Services.AddControllers();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var repo = scope.ServiceProvider.GetRequiredService<SqliteGameRepository>();
    await repo.InitializeAsync();
    var sync = scope.ServiceProvider.GetRequiredService<CatalogSyncService>();
    await sync.PublishSnapshotToShareAsync();
}

Directory.CreateDirectory(initialOptions.CoversDirectory);
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new PhysicalFileProvider(Path.GetFullPath(initialOptions.CoversDirectory)),
    RequestPath = "/covers"
});

app.UseMiddleware<AdminWorkstationMiddleware>();
app.MapControllers();

app.Run();
