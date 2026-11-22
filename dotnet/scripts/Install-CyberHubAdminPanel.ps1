<#
.SYNOPSIS
    Installs CyberHub.AdminPanel.exe ONLY on the designated CYBER HUB administration workstation
    and creates the CYBER HUB ADMIN desktop shortcut.
.DESCRIPTION
    NEVER run this script on customer PCs or bake the Admin Panel into a shared CCBoot client image.
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$AdminPublishFolder,
    [string]$InstallDir = "C:\Program Files\CyberHub\AdminPanel",
    [string]$ExpectedWorkstationName = "ADMIN-PC",
    [string]$ServerBaseUrl = "https://ccboot-srv.lan:8443"
)

$ErrorActionPreference = "Stop"

$currentHost = $env:COMPUTERNAME
if ($currentHost -ne $ExpectedWorkstationName) {
    throw "Safety Abort: This machine is '$currentHost', expected '$ExpectedWorkstationName'. NEVER install CyberHub.AdminPanel.exe on customer stations."
}

$exeSource = Join-Path $AdminPublishFolder "CyberHub.AdminPanel.exe"
if (-not (Test-Path $exeSource)) {
    throw "Could not find $exeSource. Run 'dotnet publish src\CyberHub.AdminPanel\CyberHub.AdminPanel.csproj -c Release -r win-x64 --self-contained false' first."
}

New-Item -ItemType Directory -Force -Path $InstallDir | Out-Null
Copy-Item -Path (Join-Path $AdminPublishFolder "*") -Destination $InstallDir -Recurse -Force

$configPath = Join-Path $InstallDir "admin-config.json"
$configObj = @{
    ServerBaseUrl = $ServerBaseUrl
    AllowSelfSignedLanCert = $true
    RequestTimeoutSeconds = 15
}
$configObj | ConvertTo-Json | Set-Content -Path $configPath -Encoding UTF8

$desktopPath = [Environment]::GetFolderPath("CommonDesktopDirectory")
$shortcutPath = Join-Path $desktopPath "CYBER HUB ADMIN.lnk"

$wshShell = New-Object -ComObject WScript.Shell
$shortcut = $wshShell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = (Join-Path $InstallDir "CyberHub.AdminPanel.exe")
$shortcut.WorkingDirectory = $InstallDir
$shortcut.Description = "CYBER HUB Central Administration Console"
$shortcut.Save()

Write-Host "Installed CyberHub.AdminPanel.exe to $InstallDir" -ForegroundColor Green
Write-Host "Created desktop shortcut: $shortcutPath" -ForegroundColor Green
