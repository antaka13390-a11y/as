<#
.SYNOPSIS
    Deploys CyberHub.ClientLauncher into a CCBoot Windows 10 image (Super Client mode).
.NOTES
    Run ONLY on the single CCBoot Super Client test machine or image template.
    Never copy CyberHub.AdminPanel onto any client image.
#>
param(
    [string]$SourceClientFolder = "\\CCBOOT-SRV\Deploy$\CyberHubClient",
    [string]$TargetFolder = "C:\Program Files\CyberHub\ClientLauncher",
    [switch]$RegisterAutoStart
)

$ErrorActionPreference = "Stop"

if (Test-Path (Join-Path $SourceClientFolder "CyberHub.AdminPanel.exe")) {
    throw "SECURITY ABORT: AdminPanel binary detected in client deployment folder!"
}

New-Item -ItemType Directory -Force -Path $TargetFolder | Out-Null
Copy-Item -Path "$SourceClientFolder\*" -Destination $TargetFolder -Recurse -Force

if ($RegisterAutoStart) {
    $exe = Join-Path $TargetFolder "CyberHub.ClientLauncher.exe"
    $action = New-ScheduledTaskAction -Execute $exe
    $trigger = New-ScheduledTaskTrigger -AtLogOn
    $principal = New-ScheduledTaskPrincipal -GroupId "BUILTIN\Users" -RunLevel Limited
    Register-ScheduledTask -TaskName "CyberHubClientLauncher" -Action $action -Trigger $trigger -Principal $principal -Force | Out-Null
    Write-Host "Registered CyberHubClientLauncher logon task (non-elevated)." -ForegroundColor Green
}

Write-Host "ClientLauncher deployed to $TargetFolder." -ForegroundColor Green
