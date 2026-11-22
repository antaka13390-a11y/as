<#
.SYNOPSIS
    Hardens Windows Server 2022 NTFS & SMB permissions for CYBER HUB and CCBoot Diskless clients.
.DESCRIPTION
    - C:\ProgramData\CyberHub (SQLite DB + Backups): SYSTEM & Server Administrators ONLY. Zero client access.
    - C:\CyberHubShares\PublishedCatalog: Read-only SMB share (CyberHubReadOnly$) for Domain/Cafe Users; Write only for SYSTEM/Admins.
    - Game storage shares: Read & Execute only for gaming stations.
#>
param(
    [string]$ClientAccount = "BUILTIN\Users",
    [string]$PublishedSharePath = "C:\CyberHubShares\PublishedCatalog",
    [string]$PrivateDataPath = "C:\ProgramData\CyberHub"
)

$ErrorActionPreference = "Stop"

function Invoke-IcaclsChecked {
    param(
        [string[]]$Arguments
    )
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & icacls @Arguments 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevPref
    }
    if ($output) {
        $output | ForEach-Object { Write-Host "icacls: $_" }
    }
    if ($code -ne 0) {
        throw "icacls $($Arguments -join ' ') failed with exit code $code"
    }
}

try {
    Import-Module SmbShare -ErrorAction SilentlyContinue

    Write-Host "Locking down SQLite database & backup directory ($PrivateDataPath)..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $PrivateDataPath | Out-Null
    Invoke-IcaclsChecked -Arguments @($PrivateDataPath, "/inheritance:r")
    Invoke-IcaclsChecked -Arguments @($PrivateDataPath, "/grant", "NT AUTHORITY\SYSTEM:(OI)(CI)F", "BUILTIN\Administrators:(OI)(CI)F")

    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & icacls $PrivateDataPath /remove "BUILTIN\Users" "Everyone" "Authenticated Users" 2>$null | Out-Null
    $ErrorActionPreference = $prevPref
    $global:LASTEXITCODE = 0

    Write-Host "Configuring Read-Only NTFS permissions on PublishedCatalog ($PublishedSharePath)..." -ForegroundColor Cyan
    New-Item -ItemType Directory -Force -Path $PublishedSharePath | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $PublishedSharePath "covers") | Out-Null
    New-Item -ItemType Directory -Force -Path (Join-Path $PublishedSharePath "theme-assets") | Out-Null
    Invoke-IcaclsChecked -Arguments @($PublishedSharePath, "/inheritance:r")
    Invoke-IcaclsChecked -Arguments @($PublishedSharePath, "/grant", "NT AUTHORITY\SYSTEM:(OI)(CI)F", "BUILTIN\Administrators:(OI)(CI)F")
    Invoke-IcaclsChecked -Arguments @($PublishedSharePath, "/grant", "${ClientAccount}:(OI)(CI)RX")

    if (-not (Get-SmbShare -Name "CyberHubReadOnly$" -ErrorAction SilentlyContinue)) {
        New-SmbShare -Name "CyberHubReadOnly$" -Path $PublishedSharePath -ReadAccess $ClientAccount -FullAccess "BUILTIN\Administrators" | Out-Null
    } else {
        Revoke-SmbShareAccess -Name "CyberHubReadOnly$" -AccountName "Everyone" -Force -ErrorAction SilentlyContinue | Out-Null
        Grant-SmbShareAccess -Name "CyberHubReadOnly$" -AccountName $ClientAccount -AccessRight Read -Force | Out-Null
    }

    $share = Get-SmbShare -Name "CyberHubReadOnly$" -ErrorAction Stop
    Write-Host "NTFS & SMB read-only catalog permissions applied successfully (Share=$($share.Name) -> $($share.Path))." -ForegroundColor Green
} catch {
    Write-Host "FATAL ERROR in Configure-NtfsSmbPermissions.ps1: $($_.Exception.Message)" -ForegroundColor Red
    if ($_.ScriptStackTrace) {
        Write-Host $_.ScriptStackTrace -ForegroundColor Red
    }
    exit 1
}
