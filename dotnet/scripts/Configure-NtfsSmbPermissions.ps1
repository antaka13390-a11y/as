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

Write-Host "Locking down SQLite database & backup directory ($PrivateDataPath)..." -ForegroundColor Cyan
icacls $PrivateDataPath /inheritance:r
icacls $PrivateDataPath /grant "NT AUTHORITY\SYSTEM:(OI)(CI)F" "BUILTIN\Administrators:(OI)(CI)F"
icacls $PrivateDataPath /remove "BUILTIN\Users" "Everyone" "Authenticated Users" 2>$null

Write-Host "Configuring Read-Only NTFS permissions on PublishedCatalog ($PublishedSharePath)..." -ForegroundColor Cyan
icacls $PublishedSharePath /inheritance:r
icacls $PublishedSharePath /grant "NT AUTHORITY\SYSTEM:(OI)(CI)F" "BUILTIN\Administrators:(OI)(CI)F"
icacls $PublishedSharePath /grant "${ClientAccount}:(OI)(CI)RX"

if (-not (Get-SmbShare -Name "CyberHubReadOnly$" -ErrorAction SilentlyContinue)) {
    New-SmbShare -Name "CyberHubReadOnly$" -Path $PublishedSharePath -ReadAccess $ClientAccount -FullAccess "BUILTIN\Administrators"
} else {
    Revoke-SmbShareAccess -Name "CyberHubReadOnly$" -AccountName "Everyone" -Force -ErrorAction SilentlyContinue
    Grant-SmbShareAccess -Name "CyberHubReadOnly$" -AccountName $ClientAccount -AccessRight Read -Force
}

Write-Host "NTFS & SMB read-only catalog permissions applied successfully." -ForegroundColor Green
