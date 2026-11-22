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
    [string]$AdminWorkstationName = "ADMIN-PC",
    [string]$CertificateDnsName = "ccboot-srv.lan"
)

$ErrorActionPreference = "Stop"

Write-Host "Creating CyberHub Server directories..." -ForegroundColor Cyan
New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Data" | Out-Null
New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Backups" | Out-Null
New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Certs" | Out-Null
New-Item -ItemType Directory -Force -Path "C:\CyberHubShares\PublishedCatalog\covers" | Out-Null
New-Item -ItemType Directory -Force -Path "C:\CyberHubShares\PublishedCatalog\theme-assets" | Out-Null

# Generate a strong random signing secret stored in Machine environment variables
$existingSecret = [System.Environment]::GetEnvironmentVariable("CYBERHUB_TOKEN_SECRET", "Machine")
if ([string]::IsNullOrWhiteSpace($existingSecret)) {
    $bytes = New-Object byte[] 48
    [System.Security.Cryptography.RandomNumberGenerator]::Create().GetBytes($bytes)
    $secret = [Convert]::ToBase64String($bytes)
    [System.Environment]::SetEnvironmentVariable("CYBERHUB_TOKEN_SECRET", $secret, "Machine")
    $existingSecret = $secret
    Write-Host "Generated machine-level CYBERHUB_TOKEN_SECRET." -ForegroundColor Green
}

# Generate a LAN HTTPS PFX certificate automatically if Kestrel does not already specify one
$pfxPath = "C:\ProgramData\CyberHub\Certs\cyberhub-lan.pfx"
$pfxPassword = [Convert]::ToBase64String((1..24 | ForEach-Object { [byte](Get-Random -Minimum 33 -Maximum 122) }))

$appSettingsPath = Join-Path $PublishPath "appsettings.json"
if (Test-Path $appSettingsPath) {
    $cfg = Get-Content -Path $appSettingsPath -Raw | ConvertFrom-Json
    if ($null -ne $cfg.CyberHubServer) {
        $ips = @($cfg.CyberHubServer.AllowedAdminIps | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if (-not [string]::IsNullOrWhiteSpace($AdminWorkstationIp) -and ($ips -notcontains $AdminWorkstationIp)) {
            $ips += $AdminWorkstationIp
        }
        $cfg.CyberHubServer.AllowedAdminIps = $ips

        $hosts = @($cfg.CyberHubServer.AllowedAdminWorkstations | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if (-not [string]::IsNullOrWhiteSpace($AdminWorkstationName) -and ($hosts -notcontains $AdminWorkstationName)) {
            $hosts += $AdminWorkstationName
        }
        $cfg.CyberHubServer.AllowedAdminWorkstations = $hosts
    }

    $needsCert = $true
    if ($null -ne $cfg.Kestrel -and
        $null -ne $cfg.Kestrel.Endpoints -and
        $null -ne $cfg.Kestrel.Endpoints.Https -and
        $null -ne $cfg.Kestrel.Endpoints.Https.Certificate -and
        -not [string]::IsNullOrWhiteSpace($cfg.Kestrel.Endpoints.Https.Certificate.Path) -and
        (Test-Path $cfg.Kestrel.Endpoints.Https.Certificate.Path)) {
        $needsCert = $false
    }

    if ($needsCert) {
        $dnsNames = @($CertificateDnsName, $env:COMPUTERNAME, "localhost") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique
        $cert = New-SelfSignedCertificate `
            -DnsName $dnsNames `
            -CertStoreLocation "Cert:\LocalMachine\My" `
            -FriendlyName "CYBER HUB LAN HTTPS" `
            -NotAfter (Get-Date).AddYears(5)

        $securePwd = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePwd | Out-Null

        if ($null -eq $cfg.Kestrel) { $cfg | Add-Member -NotePropertyName "Kestrel" -NotePropertyValue ([pscustomobject]@{}) }
        if ($null -eq $cfg.Kestrel.Endpoints) { $cfg.Kestrel | Add-Member -NotePropertyName "Endpoints" -NotePropertyValue ([pscustomobject]@{}) }
        if ($null -eq $cfg.Kestrel.Endpoints.Https) { $cfg.Kestrel.Endpoints | Add-Member -NotePropertyName "Https" -NotePropertyValue ([pscustomobject]@{ Url = "https://0.0.0.0:8443" }) }

        $certObj = [pscustomobject]@{
            Path = $pfxPath
            Password = $pfxPassword
        }
        if ($null -eq $cfg.Kestrel.Endpoints.Https.Certificate) {
            $cfg.Kestrel.Endpoints.Https | Add-Member -NotePropertyName "Certificate" -NotePropertyValue $certObj
        } else {
            $cfg.Kestrel.Endpoints.Https.Certificate = $certObj
        }
        Write-Host "Generated LAN HTTPS certificate at $pfxPath." -ForegroundColor Green
    }

    $jsonOut = $cfg | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($appSettingsPath, $jsonOut, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host "Updated server configuration in $appSettingsPath." -ForegroundColor Green
}

$exePath = Join-Path $PublishPath "CyberHub.ServerService.exe"
if (-not (Test-Path $exePath)) {
    throw "ServerService executable not found at $exePath. Publish CyberHub.ServerService first."
}

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
    sc.exe delete $ServiceName | Out-Null
    Start-Sleep -Seconds 2
}

New-Service -Name $ServiceName `
    -BinaryPathName "`"$exePath`" --contentRoot `"$PublishPath`"" `
    -DisplayName "CYBER HUB Central Library Service" `
    -Description "Manages the central SQLite game library, published read-only catalog, and workstation-restricted admin API." `
    -StartupType Automatic | Out-Null

# Pass CYBERHUB_TOKEN_SECRET directly to the service environment so SCM picks it up immediately without rebooting
$svcRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
if (Test-Path $svcRegPath) {
    Set-ItemProperty -Path $svcRegPath -Name "Environment" -Type MultiString -Value @("CYBERHUB_TOKEN_SECRET=$existingSecret")
}

# Configure Windows Firewall: allow LAN clients to reach HTTPS 8443
New-NetFirewallRule -DisplayName "CYBER HUB Server HTTPS (LAN)" `
    -Direction Inbound -Protocol TCP -LocalPort 8443 -Action Allow -Profile Domain,Private -ErrorAction SilentlyContinue | Out-Null

Start-Service -Name $ServiceName
Write-Host "CyberHubServerService started. Admin workstation allowed: $AdminWorkstationName ($AdminWorkstationIp)." -ForegroundColor Green
