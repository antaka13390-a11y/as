<#
.SYNOPSIS
    Writes client-config.json inside the CyberHub.ClientLauncher installation folder
    and optionally registers the non-elevated logon scheduled task.
#>
param(
    [string]$InstallDir = "C:\Program Files\CyberHub\ClientLauncher",
    [string]$ServerBaseUrl = "https://ccboot-srv.lan:8443",
    [string]$SmbShareRoot = "\\CCBOOT-SRV\CyberHubReadOnly$",
    [switch]$RegisterAutoStart,
    [switch]$RemoveAutoStart
)

$ErrorActionPreference = "Stop"

try {
    Import-Module ScheduledTasks -ErrorAction SilentlyContinue

    if ($RemoveAutoStart) {
        Unregister-ScheduledTask -TaskName "CyberHubClientLauncher" -Confirm:$false -ErrorAction SilentlyContinue
        exit 0
    }

    if (Test-Path (Join-Path $InstallDir "CyberHub.AdminPanel.exe")) {
        throw "SECURITY ABORT: AdminPanel binary detected in client installation folder!"
    }

    if (-not (Test-Path $InstallDir)) {
        New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
    }

    $cleanUrl = $ServerBaseUrl.Trim().TrimEnd('/')
    if ([string]::IsNullOrWhiteSpace($cleanUrl)) {
        $cleanUrl = "https://ccboot-srv.lan:8443"
    }

    $cleanShare = $SmbShareRoot.Trim().TrimEnd('\')
    if ([string]::IsNullOrWhiteSpace($cleanShare)) {
        $cleanShare = "\\CCBOOT-SRV\CyberHubReadOnly$"
    }

    $configPath = Join-Path $InstallDir "client-config.json"
    $configObj = [ordered]@{
        ServerBaseUrl = $cleanUrl
        SmbReadOnlyCatalogPath = "$cleanShare\catalog.json"
        SmbReadOnlyThemePath = "$cleanShare\theme.json"
        SteamExecutablePath = "C:\Program Files (x86)\Steam\steam.exe"
        RiotClientPath = "C:\Riot Games\Riot Client\RiotClientServices.exe"
        RequestTimeoutSeconds = 8
        AllowSelfSignedLanCert = $true
    }
    $jsonOut = $configObj | ConvertTo-Json
    [System.IO.File]::WriteAllText($configPath, $jsonOut, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Configured Client Launcher -> $cleanUrl / $cleanShare ($configPath)." -ForegroundColor Green

    if ($RegisterAutoStart) {
        $exe = Join-Path $InstallDir "CyberHub.ClientLauncher.exe"
        if (Test-Path $exe) {
            $action = New-ScheduledTaskAction -Execute $exe -WorkingDirectory $InstallDir
            $trigger = New-ScheduledTaskTrigger -AtLogOn
            $principal = New-ScheduledTaskPrincipal -GroupId "BUILTIN\Users" -RunLevel Limited
            Register-ScheduledTask -TaskName "CyberHubClientLauncher" -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
            Write-Host "Registered non-elevated CyberHubClientLauncher logon task." -ForegroundColor Green
        }
    }
} catch {
    Write-Host "FATAL ERROR in Configure-CyberHubClient.ps1: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ScriptStackTrace) {
        Write-Host $_.ScriptStackTrace -ForegroundColor Red
    }
    exit 1
}
