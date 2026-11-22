<#
.SYNOPSIS
    Installs CyberHub.ServerService as a Windows Service on Windows Server 2022.
.NOTES
    Run as Administrator on the CCBoot / Windows Server 2022 host.
#>
param(
    [string]$PublishPath = "C:\CyberHub\ServerService",
    [string]$ServiceName = "CyberHubServerService",
    [string]$AdminWorkstationIp = "192.168.10.50",
    [string]$AdminWorkstationName = "ADMIN-PC"
)

$ErrorActionPreference = "Stop"

Write-Host "Creating CyberHub Server directories..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Data" | Out-Null
New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Backups" | Out-Null
New-Item -ItemType Directory -Force -Path "C:\CyberHubShares\PublishedCatalog\covers" | Out-Null

# Generate a strong random signing secret stored only in Machine environment variables
$existingSecret = [System.Environment]::GetEnvironmentVariable("CYBERHUB_TOKEN_SECRET", "Machine")
if ([string]::IsNullOrWhiteSpace($existingSecret)) {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $secret = [Convert]::ToBase64String($bytes)
    [System.Environment]::SetEnvironmentVariable("CYBERHUB_TOKEN_SECRET", $secret, "Machine")
    Write-Host "Generated machine-level CYBERHUB_TOKEN_SECRET." -ForegroundColor Green
}

$exePath = Join-Path $PublishPath "CyberHub.ServerService.exe"
if (-not (Test-Path $exePath)) {
    throw "ServerService executable not found at $exePath. Publish CyberHub.ServerService first."
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName `
    -BinaryPathName "`"$exePath`"" `
    -DisplayName "CYBER HUB Central Library Service" `
    -Description "Manages the central SQLite game library, published read-only catalog, and workstation-restricted admin API." `
    -StartupType Automatic

# Configure Windows Firewall: allow LAN clients to reach HTTPS 8443
New-NetFirewallRule -DisplayName "CYBER HUB Server HTTPS (LAN)" `
    -Direction Inbound -Protocol TCP -LocalPort 8443 -Action Allow -Profile Domain,Private -ErrorAction SilentlyContinue | Out-Null

Start-Service -Name $ServiceName
Write-Host "CyberHubServerService started. Admin workstation allowed: $AdminWorkstationName ($AdminWorkstationIp)." -ForegroundColor Green
