using CyberHub.ServerService.Configuration;
using CyberHub.ServerService.Data;
using CyberHub.ServerService.Security;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/admin")]
public sealed class AdminAuthController : ControllerBase
{
    private readonly SqliteGameRepository _repository;
    private readonly TokenService _tokenService;
    private readonly ServerOptions _options;

    public AdminAuthController(
        SqliteGameRepository repository,
        TokenService tokenService,
        IOptions<ServerOptions> options)
    {
        _repository = repository;
        _tokenService = tokenService;
        _options = options.Value;
    }

    [HttpGet("status")]
    public async Task<ActionResult<AdminStatusResponse>> GetStatus(CancellationToken ct)
    {
        string? envPasswordHash = Environment.GetEnvironmentVariable("CYBERHUB_ADMIN_HASH");
        string? dbHash = await _repository.GetAdminPasswordHashAsync(ct);
        List<GameEntry> allGames = await _repository.GetGamesAsync(includeHidden: true, ct);
        List<CategoryEntry> categories = await _repository.GetCategoriesAsync(ct);
        (_, string lastBackupUtc) = await _repository.GetCatalogMetadataAsync(ct);

        string ip = AdminWorkstationMiddleware.NormalizeIp(HttpContext.Connection.RemoteIpAddress);
        string workstation = Request.Headers["X-CyberHub-Workstation"].FirstOrDefault() ?? Environment.MachineName;

        return Ok(new AdminStatusResponse
        {
            IsConfigured = !string.IsNullOrWhiteSpace(envPasswordHash) || !string.IsNullOrWhiteSpace(dbHash),
            WorkstationAuthorized = true,
            DetectedClientIp = ip,
            DetectedWorkstation = workstation,
            TotalGames = allGames.Count,
            VisibleGames = allGames.Count(g => g.IsVisible),
            TotalCategories = categories.Count,
            LastBackupUtc = lastBackupUtc
        });
    }

    [HttpPost("setup")]
    public async Task<ActionResult<AdminLoginResponse>> SetupInitialPassword([FromBody] AdminSetupRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 10)
        {
            return BadRequest(new { error = "Administrator password must be at least 10 characters." });
        }

        string hash = PasswordHasher.HashPassword(request.Password);
        bool claimed = await _repository.SetAdminPasswordHashIfUnsetAsync(hash, ct);
        if (!claimed)
        {
            return Conflict(new { error = "Administrator password is already configured." });
        }

        string ip = AdminWorkstationMiddleware.NormalizeIp(HttpContext.Connection.RemoteIpAddress);
        string workstation = Request.Headers["X-CyberHub-Workstation"].FirstOrDefault() ?? "ADMIN-PC";

        await _repository.AppendAuditLogAsync(new AdminAuditLog
        {
            Action = "ADMIN_INITIAL_SETUP",
            WorkstationName = workstation,
            SourceIp = ip,
            Details = "Initial administrator password claimed and hashed with PBKDF2-SHA512."
        }, ct);

        TimeSpan lifetime = TimeSpan.FromHours(Math.Clamp(_options.TokenLifetimeHours, 1, 24));
        string token = _tokenService.IssueToken("CyberHubAdmin", workstation, ip, lifetime);

        return Ok(new AdminLoginResponse
        {
            AccessToken = token,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime),
            Workstation = workstation
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AdminLoginResponse>> Login([FromBody] AdminLoginRequest request, CancellationToken ct)
    {
        string? envHash = Environment.GetEnvironmentVariable("CYBERHUB_ADMIN_HASH");
        string? activeHash = !string.IsNullOrWhiteSpace(envHash)
            ? envHash
            : await _repository.GetAdminPasswordHashAsync(ct);

        if (string.IsNullOrWhiteSpace(activeHash))
        {
            return Conflict(new { error = "Administrator password not yet configured. Complete first-run setup." });
        }

        string ip = AdminWorkstationMiddleware.NormalizeIp(HttpContext.Connection.RemoteIpAddress);
        string workstation = Request.Headers["X-CyberHub-Workstation"].FirstOrDefault() ?? "ADMIN-PC";

        if (!PasswordHasher.VerifyPassword(request.Password, activeHash))
        {
            await _repository.AppendAuditLogAsync(new AdminAuditLog
            {
                Action = "ADMIN_LOGIN_FAILED",
                WorkstationName = workstation,
                SourceIp = ip,
                Details = "Rejected invalid administrator password attempt."
            }, ct);

            return Unauthorized(new { error = "Invalid administrator password." });
        }

        await _repository.AppendAuditLogAsync(new AdminAuditLog
        {
            Action = "ADMIN_LOGIN_SUCCESS",
            WorkstationName = workstation,
            SourceIp = ip,
            Details = "Administrator authenticated."
        }, ct);

        TimeSpan lifetime = TimeSpan.FromHours(Math.Clamp(_options.TokenLifetimeHours, 1, 24));
        string token = _tokenService.IssueToken("CyberHubAdmin", workstation, ip, lifetime);

        return Ok(new AdminLoginResponse
        {
            AccessToken = token,
            ExpiresAtUtc = DateTimeOffset.UtcNow.Add(lifetime),
            Workstation = workstation
        });
    }

    [HttpPost("change-password")]
    public async Task<ActionResult<ChangePasswordResponse>> ChangePassword(
        [FromBody] ChangePasswordRequest request,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.CurrentPassword))
        {
            return BadRequest(new ChangePasswordResponse
            {
                Success = false,
                RequiresRelogin = false,
                Message = "Current password is required."
            });
        }

        if (!string.Equals(request.NewPassword, request.ConfirmNewPassword, StringComparison.Ordinal))
        {
            return BadRequest(new ChangePasswordResponse
            {
                Success = false,
                RequiresRelogin = false,
                Message = "New password and confirmation do not match."
            });
        }

        string ip = HttpContext.Items["AdminSourceIp"] as string
            ?? AdminWorkstationMiddleware.NormalizeIp(HttpContext.Connection.RemoteIpAddress);
        string workstation = HttpContext.Items["AdminWorkstation"] as string
            ?? Request.Headers["X-CyberHub-Workstation"].FirstOrDefault()
            ?? "ADMIN-PC";

        (bool success, string error) = await _repository.ChangeAdminPasswordAsync(
            request.CurrentPassword,
            request.NewPassword,
            _tokenService,
            workstation,
            ip,
            ct);

        if (!success)
        {
            return BadRequest(new ChangePasswordResponse
            {
                Success = false,
                RequiresRelogin = false,
                Message = error
            });
        }

        return Ok(new ChangePasswordResponse
        {
            Success = true,
            RequiresRelogin = true,
            Message = "Administrator password updated and all active sessions revoked."
        });
    }

    [HttpPost("logout")]
    public async Task<ActionResult<object>> Logout(CancellationToken ct)
    {
        string ip = HttpContext.Items["AdminSourceIp"] as string
            ?? AdminWorkstationMiddleware.NormalizeIp(HttpContext.Connection.RemoteIpAddress);
        string workstation = HttpContext.Items["AdminWorkstation"] as string
            ?? Request.Headers["X-CyberHub-Workstation"].FirstOrDefault()
            ?? "ADMIN-PC";

        long epoch = await _repository.IncrementTokenEpochAsync(workstation, ip, ct);
        _tokenService.SetMinimumValidEpoch(epoch);
        return Ok(new { loggedOut = true });
    }
}
