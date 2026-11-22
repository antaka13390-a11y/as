<#
.SYNOPSIS
    Scheduled SQLite backup retention script (keeps last 30 daily backups).
#>
param(
    [string]$BackupDirectory = "C:\ProgramData\CyberHub\Backups",
    [string]$ServerAdminUrl = "https://127.0.0.1:8443/api/admin/system/backup",
    [int]$RetainDays = 30
)

$ErrorActionPreference = "Stop"
$cutoff = (Get-Date).AddDays(-$RetainDays)

if (Test-Path $BackupDirectory) {
    Get-ChildItem -Path $BackupDirectory -Filter "cyberhub-backup-*.db" |
        Where-Object { $_.LastWriteTime -lt $cutoff } |
        Remove-Item -Force
}

Write-Host "Backup retention cleanup complete (retained $RetainDays days)." -ForegroundColor Green
