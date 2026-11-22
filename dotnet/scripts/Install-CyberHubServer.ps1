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
    [string]$CertificateDnsName = "ccboot-srv.lan",
    [string]$OutputPfxPath = "C:\ProgramData\CyberHub\Certs\cyberhub-lan.pfx",
    [ValidateSet("LocalMachine", "CurrentUser")]
    [string]$CertStoreLocation = "LocalMachine",
    [switch]$CertificateOnly
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

function Invoke-CheckedNative {
    param(
        [string]$Executable,
        [string[]]$Arguments
    )
    $prevPref = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $output = & $Executable @Arguments 2>&1
        $code = $LASTEXITCODE
    } finally {
        $ErrorActionPreference = $prevPref
    }
    if ($output) {
        $output | ForEach-Object { Write-StepLog "$Executable`: $_" }
    }
    if ($code -ne 0) {
        throw "$Executable $($Arguments -join ' ') failed with exit code $code."
    }
}

function New-CyberHubLanCertificate {
    param(
        [string[]]$DnsNames,
        [string]$PfxPath,
        [string]$PfxPassword,
        [ValidateSet("LocalMachine", "CurrentUser")]
        [string]$StoreLocationName = "LocalMachine"
    )

    Add-Type -AssemblyName System.Core

    $cleanDns = @($DnsNames | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | ForEach-Object { $_.Trim() } | Select-Object -Unique)
    if ($cleanDns.Count -eq 0) {
        $cleanDns = @("localhost")
    }

    $pfxDir = Split-Path -Path $PfxPath -Parent
    if (-not [string]::IsNullOrWhiteSpace($pfxDir)) {
        New-Item -ItemType Directory -Force -Path $pfxDir | Out-Null
    }

    Write-StepLog "Generating RSA 2048-bit SHA-256 self-signed LAN HTTPS certificate (.NET X509Store, Store=$StoreLocationName\My) for DNS names: $($cleanDns -join ', ')..." "Cyan"

    $cngKey = $null
    $rsa = $null
    $ephemeralCert = $null
    $storeCert = $null
    $thumbprint = $null

    try {
        $cngParams = New-Object System.Security.Cryptography.CngKeyCreationParameters
        $cngParams.ExportPolicy = [System.Security.Cryptography.CngExportPolicies]::AllowExport -bor `
                                  [System.Security.Cryptography.CngExportPolicies]::AllowPlaintextExport
        $keyLengthBytes = [BitConverter]::GetBytes([int]2048)
        $cngParams.Parameters.Add(
            (New-Object System.Security.Cryptography.CngProperty(
                "Length",
                $keyLengthBytes,
                [System.Security.Cryptography.CngPropertyOptions]::None
            ))
        )

        $cngKey = [System.Security.Cryptography.CngKey]::Create(
            [System.Security.Cryptography.CngAlgorithm]::Rsa,
            $null,
            $cngParams
        )
        $rsa = New-Object System.Security.Cryptography.RSACng($cngKey)

        $primaryDns = $cleanDns[0]
        $certReq = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=$primaryDns",
            $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
        )

        $sanBuilder = New-Object System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder
        foreach ($dns in $cleanDns) {
            $sanBuilder.AddDnsName($dns)
        }
        $certReq.CertificateExtensions.Add($sanBuilder.Build())

        $certReq.CertificateExtensions.Add(
            (New-Object System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension($false, $false, 0, $false))
        )
        $keyUsage = [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor `
                    [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment
        $certReq.CertificateExtensions.Add(
            (New-Object System.Security.Cryptography.X509Certificates.X509KeyUsageExtension($keyUsage, $true))
        )
        $ekuOids = New-Object System.Security.Cryptography.OidCollection
        [void]$ekuOids.Add((New-Object System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1")))
        $certReq.CertificateExtensions.Add(
            (New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension($ekuOids, $false))
        )

        $notBefore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
        $notAfter = [DateTimeOffset]::UtcNow.AddYears(5)
        $ephemeralCert = $certReq.CreateSelfSigned($notBefore, $notAfter)
        $ephemeralCert.FriendlyName = "CYBER HUB LAN HTTPS"

        $pfxBytes = $ephemeralCert.Export([System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx, $PfxPassword)
        if ($null -eq $pfxBytes -or $pfxBytes.Length -eq 0) {
            throw "Exporting generated certificate to PFX produced zero bytes."
        }
        [System.IO.File]::WriteAllBytes($PfxPath, $pfxBytes)
        if (-not (Test-Path $PfxPath)) {
            throw "Exported PFX certificate was not written to $PfxPath."
        }

        $storeEnum = [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocationName
        $keySetFlag = if ($StoreLocationName -eq "LocalMachine") {
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet
        } else {
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
        }
        $persistFlags = $keySetFlag -bor [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet

        $storeCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $PfxPath,
            $PfxPassword,
            $persistFlags
        )
        $storeCert.FriendlyName = "CYBER HUB LAN HTTPS"
        $thumbprint = $storeCert.Thumbprint

        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            [System.Security.Cryptography.X509Certificates.StoreName]::My,
            $storeEnum
        )
        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        try {
            $store.Add($storeCert)
        } finally {
            $store.Close()
        }

        # Ensure LocalSystem service identity and Administrators can read the persisted private key file
        if ($StoreLocationName -eq "LocalMachine") {
            $privKey = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($storeCert)
            try {
                if ($null -eq $privKey) {
                    throw "Persisted certificate $thumbprint in LocalMachine\My has no accessible RSA private key."
                }
                $keyFilePath = $null
                if ($privKey -is [System.Security.Cryptography.RSACng]) {
                    $uniqueName = $privKey.Key.UniqueName
                    if (-not [string]::IsNullOrWhiteSpace($uniqueName)) {
                        $candidate = Join-Path "$env:ProgramData\Microsoft\Crypto\Keys" $uniqueName
                        if (Test-Path $candidate) { $keyFilePath = $candidate }
                    }
                } elseif ($privKey -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
                    $uniqueName = $privKey.CspKeyContainerInfo.UniqueKeyContainerName
                    if (-not [string]::IsNullOrWhiteSpace($uniqueName)) {
                        $candidate = Join-Path "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys" $uniqueName
                        if (Test-Path $candidate) { $keyFilePath = $candidate }
                    }
                }
                if ($keyFilePath) {
                    Write-StepLog "Granting private key read permission for NT AUTHORITY\SYSTEM at $keyFilePath..."
                    Invoke-CheckedNative -Executable "icacls" -Arguments @($keyFilePath, "/grant", "NT AUTHORITY\SYSTEM:(R)", "BUILTIN\Administrators:(F)")
                } else {
                    Write-StepLog "Note: Private key file path not resolved on disk; LocalSystem default machine-key access applies."
                }
            } finally {
                if ($null -ne $privKey) { $privKey.Dispose() }
            }
        }
    } finally {
        if ($null -ne $storeCert) { $storeCert.Dispose() }
        if ($null -ne $ephemeralCert) { $ephemeralCert.Dispose() }
        if ($null -ne $rsa) { $rsa.Dispose() }
        if ($null -ne $cngKey) { $cngKey.Dispose() }
    }

    # Validate the generated PFX file independently
    $verifyFlags = if ($StoreLocationName -eq "LocalMachine") {
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet
    } else {
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
    }
    $verifyPfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($PfxPath, $PfxPassword, $verifyFlags)
    try {
        if (-not $verifyPfx.HasPrivateKey) {
            throw "PFX validation failed: certificate at $PfxPath has no private key."
        }
        $pubRsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($verifyPfx)
        try {
            if ($null -eq $pubRsa -or $pubRsa.KeySize -ne 2048) {
                throw "PFX validation failed: expected RSA 2048-bit key, found $($pubRsa.KeySize)."
            }
        } finally {
            if ($null -ne $pubRsa) { $pubRsa.Dispose() }
        }
        if ($verifyPfx.SignatureAlgorithm.FriendlyName -notmatch "sha256") {
            throw "PFX validation failed: expected SHA-256 signature algorithm, found $($verifyPfx.SignatureAlgorithm.FriendlyName)."
        }
        $sanExt = $verifyPfx.Extensions | Where-Object { $_.Oid.Value -eq "2.5.29.17" } | Select-Object -First 1
        if ($null -eq $sanExt) {
            throw "PFX validation failed: Subject Alternative Name extension is missing."
        }
        $sanFormatted = $sanExt.Format($false)
        foreach ($dns in $cleanDns) {
            if ($sanFormatted -notmatch [regex]::Escape($dns)) {
                throw "PFX validation failed: SAN '$sanFormatted' does not include expected DNS name '$dns'."
            }
        }
    } finally {
        $verifyPfx.Dispose()
    }

    # Validate retrieval from the target certificate store without relying on Cert: PSDrive
    $verifyStoreEnum = [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocationName
    $verifyStore = New-Object System.Security.Cryptography.X509Certificates.X509Store(
        [System.Security.Cryptography.X509Certificates.StoreName]::My,
        $verifyStoreEnum
    )
    $verifyStore.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadOnly)
    try {
        $matches = $verifyStore.Certificates.Find(
            [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
            $thumbprint,
            $false
        )
        if ($matches.Count -lt 1) {
            throw "Store validation failed: thumbprint $thumbprint was not found in $StoreLocationName\My."
        }
        $storedCert = $matches[0]
        if (-not $storedCert.HasPrivateKey) {
            throw "Store validation failed: stored certificate $thumbprint in $StoreLocationName\My has no private key."
        }
        $storedPriv = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPrivateKey($storedCert)
        try {
            if ($null -eq $storedPriv) {
                throw "Store validation failed: unable to acquire RSA private key handle for $thumbprint."
            }
        } finally {
            if ($null -ne $storedPriv) { $storedPriv.Dispose() }
        }
    } finally {
        $verifyStore.Close()
    }

    Write-StepLog "Validated LAN HTTPS certificate in $StoreLocationName\My and PFX '$PfxPath' (Thumbprint=$thumbprint, RSA=2048, Hash=SHA256)." "Green"
    return $thumbprint
}

try {
    Write-StepLog "=== CYBER HUB Server Installation Started ===" "Cyan"
    Write-StepLog "User: $([System.Security.Principal.WindowsIdentity]::GetCurrent().Name) | Machine: $env:COMPUTERNAME | PSVersion: $($PSVersionTable.PSVersion)"
    Write-StepLog "Parameters: PublishPath='$PublishPath' ServiceName='$ServiceName' AdminIp='$AdminWorkstationIp' AdminHost='$AdminWorkstationName' CertificateDnsName='$CertificateDnsName' CertificateOnly=$CertificateOnly"

    # Generate a cryptographically random PFX password using an explicit byte[]
    $pfxBytes = New-Object byte[] 24
    $pfxRng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    try {
        $pfxRng.GetBytes($pfxBytes)
    } finally {
        $pfxRng.Dispose()
    }
    $pfxPassword = [Convert]::ToBase64String($pfxBytes)
    $pfxPath = $OutputPfxPath

    if ($CertificateOnly) {
        $dnsList = @($CertificateDnsName, $env:COMPUTERNAME, "localhost") | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
        $thumb = New-CyberHubLanCertificate -DnsNames $dnsList -PfxPath $pfxPath -PfxPassword $pfxPassword -StoreLocationName $CertStoreLocation
        Write-Host "CYBERHUB_CERT_THUMBPRINT=$thumb"
        Write-Host "CYBERHUB_PFX_PATH=$pfxPath"
        exit 0
    }

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
        $dnsNames = @($CertificateDnsName, $env:COMPUTERNAME, "localhost") |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
            ForEach-Object { $_.Trim() } |
            Select-Object -Unique

        $certThumbprint = New-CyberHubLanCertificate `
            -DnsNames $dnsNames `
            -PfxPath $pfxPath `
            -PfxPassword $pfxPassword `
            -StoreLocationName $CertStoreLocation

        if ($null -eq $cfg.Kestrel) { $cfg | Add-Member -NotePropertyName "Kestrel" -NotePropertyValue ([pscustomobject]@{}) -Force }
        if ($null -eq $cfg.Kestrel.Endpoints) { $cfg.Kestrel | Add-Member -NotePropertyName "Endpoints" -NotePropertyValue ([pscustomobject]@{}) -Force }
        if ($null -eq $cfg.Kestrel.Endpoints.Https) { $cfg.Kestrel.Endpoints | Add-Member -NotePropertyName "Https" -NotePropertyValue ([pscustomobject]@{ Url = "https://0.0.0.0:8443" }) -Force }

        $certObj = [pscustomobject]@{
            Path = $pfxPath
            Password = $pfxPassword
        }
        $cfg.Kestrel.Endpoints.Https | Add-Member -NotePropertyName "Certificate" -NotePropertyValue $certObj -Force
        Write-StepLog "Configured Kestrel HTTPS certificate '$pfxPath' (Thumbprint=$certThumbprint)." "Green"
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
        Invoke-CheckedNative -Executable "sc.exe" -Arguments @("delete", $ServiceName)
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

    Import-Module CimCmdlets -ErrorAction SilentlyContinue
    $registeredSvc = Get-CimInstance -ClassName Win32_Service -Filter "Name='$ServiceName'" -ErrorAction SilentlyContinue
    if ($null -eq $registeredSvc) {
        throw "Service '$ServiceName' could not be queried from Win32_Service immediately after New-Service."
    }
    Write-StepLog "Verified registration -> Name=$($registeredSvc.Name) | StartMode=$($registeredSvc.StartMode) | Account=$($registeredSvc.StartName) | PathName=$($registeredSvc.PathName)" "Green"

    # Configure Windows Firewall: allow LAN clients to reach HTTPS 8443
    Import-Module NetSecurity -ErrorAction SilentlyContinue
    if (Get-Command New-NetFirewallRule -ErrorAction SilentlyContinue) {
        $existingRule = Get-NetFirewallRule -DisplayName "CYBER HUB Server HTTPS (LAN)" -ErrorAction SilentlyContinue
        if (-not $existingRule) {
            New-NetFirewallRule -DisplayName "CYBER HUB Server HTTPS (LAN)" `
                -Direction Inbound -Protocol TCP -LocalPort 8443 -Action Allow -Profile Domain,Private -ErrorAction SilentlyContinue | Out-Null
            Write-StepLog "Created Windows Firewall rule 'CYBER HUB Server HTTPS (LAN)' for TCP 8443."
        } else {
            Write-StepLog "Windows Firewall rule 'CYBER HUB Server HTTPS (LAN)' already exists."
        }
    } else {
        Write-StepLog "NetSecurity module unavailable; adding firewall rule via netsh advfirewall..."
        $prevPref = $ErrorActionPreference
        $ErrorActionPreference = "Continue"
        & netsh advfirewall firewall delete rule name="CYBER HUB Server HTTPS (LAN)" 2>$null | Out-Null
        & netsh advfirewall firewall add rule name="CYBER HUB Server HTTPS (LAN)" dir=in action=allow protocol=TCP localport=8443 profile=domain,private | Out-Null
        $ErrorActionPreference = $prevPref
        $global:LASTEXITCODE = 0
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
