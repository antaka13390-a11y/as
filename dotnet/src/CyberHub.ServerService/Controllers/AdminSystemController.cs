using CyberHub.ServerService.Data;
using CyberHub.Shared.Models;
using Microsoft.AspNetCore.Mvc;

namespace CyberHub.ServerService.Controllers;

[ApiController]
[Route("api/admin/system")]
public sealed class AdminSystemController : ControllerBase
{
    private readonly SqliteGameRepository _repository;

    public AdminSystemController(SqliteGameRepository repository)
    {
        _repository = repository;
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
}
