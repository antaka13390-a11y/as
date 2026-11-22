<#
.SYNOPSIS
    Writes admin-config.json inside the CyberHub.AdminPanel installation folder.
#>
param(
    [string]$InstallDir = "C:\Program Files\CyberHub\AdminPanel",
    [string]$ServerBaseUrl = "https://ccboot-srv.lan:8443"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $InstallDir)) {
    New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
}

$cleanUrl = $ServerBaseUrl.Trim().TrimEnd('/')
if ([string]::IsNullOrWhiteSpace($cleanUrl)) {
    $cleanUrl = "https://ccboot-srv.lan:8443"
}

$configPath = Join-Path $InstallDir "admin-config.json"
$configObj = [ordered]@{
    ServerBaseUrl = $cleanUrl
    AllowSelfSignedLanCert = $true
    RequestTimeoutSeconds = 15
}
$jsonOut = $configObj | ConvertTo-Json
[System.IO.File]::WriteAllText($configPath, $jsonOut, (New-Object System.Text.UTF8Encoding($false)))
Write-Host "Configured Admin Panel connection -> $cleanUrl ($configPath)." -ForegroundColor Green
