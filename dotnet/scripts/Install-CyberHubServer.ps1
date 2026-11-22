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

function Write-StepLog {
    param(
        [string]$Message,
        [string]$Color = "Gray"
    )
    $ts = (Get-Date).ToString("yyyy-MM-dd HH:mm:ss")
    Write-Host "[$ts] $Message" -ForegroundColor $Color
}

try {
    Write-StepLog "=== CYBER HUB Server Installation Started ===" "Cyan"
    Write-StepLog "User: $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) | Machine: $env:COMPUTERNAME | PSVersion: $($PSVersionTable.PSVersion)"
    Write-StepLog "Parameters: PublishPath='$PublishPath' ServiceName='$ServiceName' AdminIp='$AdminWorkstationIp' AdminHost='$AdminWorkstationName' CertificateDnsName='$CertificateDnsName'"

    $principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Install-CyberHubServer.ps1 must run with Administrator privileges to register '$ServiceName'."
    }

    Write-StepLog "Creating CyberHub Server directories..." "Cyan"
    New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Data" | Out-Null
    New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Backups" | Out-Null
    New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Certs" | Out-Null
    New-Item -ItemType Directory -Force -Path "C:\ProgramData\CyberHub\Logs" | Out-Null
    New-Item -ItemType Directory -Force -Path "C:\CyberHubShares\PublishedCatalog\covers" | Out-Null
    New-Item -ItemType Directory -Force -Path "C:\CyberHubShares\PublishedCatalog\theme-assets" | Out-Null

    # Generate a strong random signing secret stored in Machine environment variables
    $existingSecret = [System.Environment]::GetEnvironmentVariable("CYBERHUB_TOKEN_SECRET", "Machine")
    if ([string]::IsNullOrWhiteSpace($existingSecret)) {
        $bytes = New-Object byte[] 48
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        try {
            $rng.GetBytes($bytes)
        } finally {
            $rng.Dispose()
        }
        $secret = [Convert]::ToBase64String($bytes)
        [System.Environment]::SetEnvironmentVariable("CYBERHUB_TOKEN_SECRET", $secret, "Machine")
        $existingSecret = $secret
        Write-StepLog "Generated machine-level CYBERHUB_TOKEN_SECRET." "Green"
    } else {
        Write-StepLog "Using existing machine-level CYBERHUB_TOKEN_SECRET."
    }

    # Generate a cryptographically random PFX password using an explicit byte[]
    $pfxBytes = New-Object byte[] 24
    $pfxRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $pfxRng.GetBytes($pfxBytes)
    } finally {
        $pfxRng.Dispose()
    }
    $pfxPassword = [Convert]::ToBase64String($pfxBytes)
    $pfxPath = "C:\ProgramData\CyberHub\Certs\cyberhub-lan.pfx"

    $appSettingsPath = Join-Path $PublishPath "appsettings.json"
    if (-not (Test-Path $appSettingsPath)) {
        throw "Expected server configuration file not found at $appSettingsPath."
    }

    $cfg = Get-Content -Path $appSettingsPath -Raw | ConvertFrom-Json
    if ($null -ne $cfg.CyberHubServer) {
        $ips = @($cfg.CyberHubServer.AllowedAdminIps | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if (-not [string]::IsNullOrWhiteSpace($AdminWorkstationIp) -and ($ips -notcontains $AdminWorkstationIp)) {
            $ips += $AdminWorkstationIp
        }
        $cfg.CyberHubServer.AllowedAdminIps = @($ips)

        $hosts = @($cfg.CyberHubServer.AllowedAdminWorkstations | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
        if (-not [string]::IsNullOrWhiteSpace($AdminWorkstationName) -and ($hosts -notcontains $AdminWorkstationName)) {
            $hosts += $AdminWorkstationName
        }
        $cfg.CyberHubServer.AllowedAdminWorkstations = @($hosts)
    }

    $needsCert = $true
    if ($null -ne $cfg.Kestrel -and
        $null -ne $cfg.Kestrel.Endpoints -and
        $null -ne $cfg.Kestrel.Endpoints.Https -and
        $null -ne $cfg.Kestrel.Endpoints.Https.Certificate -and
        -not [string]::IsNullOrWhiteSpace($cfg.Kestrel.Endpoints.Https.Certificate.Path) -and
        (Test-Path $cfg.Kestrel.Endpoints.Https.Certificate.Path)) {
        $needsCert = $false
        Write-StepLog "Existing LAN HTTPS certificate found at $($cfg.Kestrel.Endpoints.Https.Certificate.Path)."
    }

    if ($needsCert) {
        [string[]]$dnsNames = @($CertificateDnsName, $env:COMPUTERNAME, "localhost") |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() } |
            Select-Object -Unique
        Write-StepLog "Generating self-signed LAN HTTPS certificate for DNS names: $($dnsNames -join ', ')..." "Cyan"

        Import-Module PKI -ErrorAction SilentlyContinue
        $cert = New-SelfSignedCertificate `
            -DnsName $dnsNames `
            -CertStoreLocation "Cert:\LocalMachine\My" `
            -FriendlyName "CYBER HUB LAN HTTPS" `
            -KeyExportPolicy Exportable `
            -KeyAlgorithm RSA `
            -KeyLength 2048 `
            -HashAlgorithm SHA256 `
            -NotAfter (Get-Date).AddYears(5)

        if ($null -eq $cert) {
            throw "New-SelfSignedCertificate returned null."
        }

        $securePwd = ConvertTo-SecureString -String $pfxPassword -Force -AsPlainText
        Export-PfxCertificate -Cert $cert -FilePath $pfxPath -Password $securePwd | Out-Null
        if (-not (Test-Path $pfxPath)) {
            throw "Exported PFX certificate was not created at $pfxPath."
        }

        if ($null -eq $cfg.Kestrel) { $cfg | Add-Member -NotePropertyName "Kestrel" -NotePropertyValue ([pscustomobject]@{}) -Force }
        if ($null -eq $cfg.Kestrel.Endpoints) { $cfg.Kestrel | Add-Member -NotePropertyName "Endpoints" -NotePropertyValue ([pscustomobject]@{}) -Force }
        if ($null -eq $cfg.Kestrel.Endpoints.Https) { $cfg.Kestrel.Endpoints | Add-Member -NotePropertyName "Https" -NotePropertyValue ([pscustomobject]@{ Url = "https://0.0.0.0:8443" }) -Force }

        $certObj = [pscustomobject]@{
            Path = $pfxPath
            Password = $pfxPassword
        }
        $cfg.Kestrel.Endpoints.Https | Add-Member -NotePropertyName "Certificate" -NotePropertyValue $certObj -Force
        Write-StepLog "Generated LAN HTTPS certificate at $pfxPath." "Green"
    }

    $jsonOut = $cfg | ConvertTo-Json -Depth 8
    [System.IO.File]::WriteAllText($appSettingsPath, $jsonOut, (New-Object System.Text.UTF8Encoding($false)))
    Write-StepLog "Updated server configuration in $appSettingsPath." "Green"

    $exePath = Join-Path $PublishPath "CyberHub.ServerService.exe"
    if (-not (Test-Path $exePath)) {
        throw "ServerService executable not found at $exePath. Publish CyberHub.ServerService first."
    }

    $existingSvc = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existingSvc) {
        Write-StepLog "Removing existing service '$ServiceName' (Status: $($existingSvc.Status))..." "Yellow"
        Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
        & sc.exe delete $ServiceName 2>&1 | ForEach-Object { Write-StepLog "sc delete: $_" }
        $wait = 0
        while ((Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) -and $wait -lt 30) {
            Start-Sleep -Milliseconds 500
            $wait++
        }
        if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
            throw "Timed out waiting for existing service '$ServiceName' deletion to complete."
        }
    }

    $binPath = "`"$exePath`" --contentRoot `"$PublishPath`""
    Write-StepLog "Registering Windows service '$ServiceName' with BinaryPathName: $binPath" "Cyan"
    New-Service -Name $ServiceName `
        -BinaryPathName $binPath `
        -DisplayName "CYBER HUB Central Library Service" `
        -Description "Manages the central SQLite game library, published read-only catalog, and workstation-restricted admin API." `
        -StartupType Automatic | Out-Null

    # Pass CYBERHUB_TOKEN_SECRET directly to the service environment so SCM picks it up immediately without rebooting
    $svcRegPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
    if (-not (Test-Path $svcRegPath)) {
        throw "Service registry key was not created at $svcRegPath after New-Service."
    }
    Set-ItemProperty -Path $svcRegPath -Name "Environment" -Type MultiString -Value @("CYBERHUB_TOKEN_SECRET=$existingSecret")

    $registeredSvc = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    if ($null -eq $registeredSvc) {
        throw "Service '$ServiceName' could not be queried from Win32_Service immediately after New-Service."
    }
    Write-StepLog "Verified registration -> Name=$($registeredSvc.Name) | StartMode=$($registeredSvc.StartMode) | Account=$($registeredSvc.StartName) | PathName=$($registeredSvc.PathName)" "Green"

    # Configure Windows Firewall: allow LAN clients to reach HTTPS 8443
    $existingRule = Get-NetFirewallRule -DisplayName "CYBER HUB Server HTTPS (LAN)" -ErrorAction SilentlyContinue
    if (-not $existingRule) {
        New-NetFirewallRule -DisplayName "CYBER HUB Server HTTPS (LAN)" `
            -Direction Inbound -Protocol TCP -LocalPort 8443 -Action Allow -Profile Domain,Private -ErrorAction SilentlyContinue | Out-Null
        Write-StepLog "Created Windows Firewall rule 'CYBER HUB Server HTTPS (LAN)' for TCP 8443."
    } else {
        Write-StepLog "Windows Firewall rule 'CYBER HUB Server HTTPS (LAN)' already exists."
    }

    Write-StepLog "Starting Windows service '$ServiceName'..." "Cyan"
    try {
        Start-Service -Name $ServiceName
        $svc = Get-Service -Name $ServiceName -ErrorAction Stop
        $svc.WaitForStatus([System.ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(20))
        $svc.Refresh()
        Write-StepLog "CyberHubServerService started (Status=$($svc.Status)). Admin workstation allowed: $AdminWorkstationName ($AdminWorkstationIp)." "Green"
    } catch {
        Write-StepLog "Service startup failed: $($_.Exception.Message)" "Red"
        Write-StepLog "Dumping recent System & Application event log errors for diagnostics..." "Yellow"
        Get-WinEvent -FilterHashtable @{ LogName = 'System', 'Application'; Level = 1, 2, 3 } -MaxEvents 20 -ErrorAction SilentlyContinue |
            Where-Object { $_.Message -match 'CyberHub|Service Control Manager|\.NET Runtime|Application Error' -or $_.ProviderName -match 'Service Control Manager|\.NET Runtime|Application Error' } |
            Select-Object -First 10 |
            ForEach-Object { Write-StepLog "Event [$($_.TimeCreated)] [$($_.LogName)/$($_.ProviderName)]: $($_.Message)" "Yellow" }
        throw
    }

    Write-StepLog "=== CYBER HUB Server Installation Completed Successfully ===" "Green"
} catch {
    Write-StepLog "FATAL ERROR in Install-CyberHubServer.ps1: $($_.Exception.Message)" "Red"
    if ($_.ScriptStackTrace) {
        Write-StepLog "StackTrace: $($_.ScriptStackTrace)" "Red"
    }
    exit 1
}
