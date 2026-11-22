using CyberHub.ServerService.Data;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using Microsoft.AspNetCore.Mvc;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/admin/system")]
public sealed class AdminSystemController : ControllerBase
{
    private readonly SqliteGameRepository _repository;
    private readonly RecoveryCodeService _recoveryCodeService;

    public AdminSystemController(SqliteGameRepository repository, RecoveryCodeService recoveryCodeService)
    {
        _repository = repository;
        _recoveryCodeService = recoveryCodeService;
    }

    [HttpGet("logs")]
    public async Task<ActionResult<List<AdminAuditLog>>> GetLogs([FromQuery] int limit = 200, CancellationToken ct = default)
    {
        return Ok(await _repository.GetAuditLogsAsync(limit, ct));
    }

    [HttpPost("backup")]
    public async Task<ActionResult<BackupResult>> CreateBackup(CancellationToken ct)
    {
        BackupResult result = await _repository.CreateOnlineBackupAsync(ct);

        string workstation = HttpContext.Items["AdminWorkstation"] as string ?? "ADMIN-PC";
        string ip = HttpContext.Items["AdminSourceIp"] as string ?? "127.0.0.1";

        await _repository.AppendAuditLogAsync(new AdminAuditLog
        {
            Action = "DATABASE_BACKUP",
            WorkstationName = workstation,
            SourceIp = ip,
            Details = $"Created SQLite online backup ({result.SizeBytes} bytes) at {Path.GetFileName(result.BackupFilePath)}"
        }, ct);

        return Ok(result);
    }

    [HttpPost("recovery-code")]
    public async Task<ActionResult<RecoveryCodeResponse>> IssueRecoveryCode([FromQuery] string stationName, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(stationName) || stationName.Trim().Length > 64)
        {
            return BadRequest(new { error = "Valid target station name is required." });
        }

        string cleanStation = stationName.Trim().ToUpperInvariant();
        (string code, DateTimeOffset expires) = _recoveryCodeService.IssueStationRecoveryCode(cleanStation);

        string workstation = HttpContext.Items["AdminWorkstation"] as string ?? "ADMIN-PC";
        string ip = HttpContext.Items["AdminSourceIp"] as string ?? "127.0.0.1";

        await _repository.AppendAuditLogAsync(new AdminAuditLog
        {
            Action = "SESSION_RECOVERY_ISSUED",
            WorkstationName = workstation,
            SourceIp = ip,
            Details = $"Issued 15-minute session recovery code for client station {cleanStation}."
        }, ct);

        return Ok(new RecoveryCodeResponse
        {
            StationName = cleanStation,
            RecoveryCode = code,
            ExpiresAtUtc = expires
        });
    }
}
