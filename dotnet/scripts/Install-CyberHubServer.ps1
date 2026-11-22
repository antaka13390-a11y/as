
function New-CyberHubLanCertificate {
    param(
        [string[]]$DnsNames,
        [string]$PfxPath,
        [string]$PfxPassword,
        [ValidateSet("LocalMachine", "CurrentUser")]
        [string]$StoreLocationName = "LocalMachine"
    )

    Add-Type -AssemblyName System.Core

    $cleanDns = @(
        $DnsNames |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        ForEach-Object { $_.Trim() } |
        Select-Object -Unique
    )

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
    $store = $null

    try {
        # Create an exportable ephemeral RSA key.
        # EphemeralKey prevents persistent CNG key-name collisions.
        $cngParams = New-Object System.Security.Cryptography.CngKeyCreationParameters

        $cngParams.Provider = [System.Security.Cryptography.CngProvider]::MicrosoftSoftwareKeyStorageProvider

        $cngParams.KeyCreationOptions = [System.Security.Cryptography.CngKeyCreationOptions]::EphemeralKey

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

        # Build certificate request.
        $primaryDns = $cleanDns[0]

        $certReq = New-Object System.Security.Cryptography.X509Certificates.CertificateRequest(
            "CN=$primaryDns",
            $rsa,
            [System.Security.Cryptography.HashAlgorithmName]::SHA256,
            [System.Security.Cryptography.RSASignaturePadding]::Pkcs1
        )

        # Subject Alternative Names.
        $sanBuilder = New-Object System.Security.Cryptography.X509Certificates.SubjectAlternativeNameBuilder

        foreach ($dns in $cleanDns) {
            $sanBuilder.AddDnsName($dns)
        }

        $certReq.CertificateExtensions.Add($sanBuilder.Build())

        # Basic Constraints.
        $certReq.CertificateExtensions.Add(
            (New-Object System.Security.Cryptography.X509Certificates.X509BasicConstraintsExtension(
                $false,
                $false,
                0,
                $false
            ))
        )

        # Key Usage.
        $keyUsage = [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::DigitalSignature -bor `
                    [System.Security.Cryptography.X509Certificates.X509KeyUsageFlags]::KeyEncipherment

        $certReq.CertificateExtensions.Add(
            (New-Object System.Security.Cryptography.X509Certificates.X509KeyUsageExtension(
                $keyUsage,
                $true
            ))
        )

        # Server Authentication EKU.
        $ekuOids = New-Object System.Security.Cryptography.OidCollection

        [void]$ekuOids.Add(
            (New-Object System.Security.Cryptography.Oid("1.3.6.1.5.5.7.3.1"))
        )

        $certReq.CertificateExtensions.Add(
            (New-Object System.Security.Cryptography.X509Certificates.X509EnhancedKeyUsageExtension(
                $ekuOids,
                $false
            ))
        )

        # Certificate validity: 5 years.
        $notBefore = [DateTimeOffset]::UtcNow.AddMinutes(-5)
        $notAfter = [DateTimeOffset]::UtcNow.AddYears(5)

        $ephemeralCert = $certReq.CreateSelfSigned($notBefore, $notAfter)
        $ephemeralCert.FriendlyName = "CYBER HUB LAN HTTPS"

        # Export ephemeral certificate and private key to PFX.
        $pfxBytes = $ephemeralCert.Export(
            [System.Security.Cryptography.X509Certificates.X509ContentType]::Pfx,
            $PfxPassword
        )

        if ($null -eq $pfxBytes -or $pfxBytes.Length -eq 0) {
            throw "Exporting generated certificate to PFX produced zero bytes."
        }

        [System.IO.File]::WriteAllBytes($PfxPath, $pfxBytes)

        if (-not (Test-Path $PfxPath)) {
            throw "Exported PFX certificate was not written to $PfxPath."
        }

        # Import PFX using persistent machine/user key storage.
        $storeEnum = [System.Security.Cryptography.X509Certificates.StoreLocation]::$StoreLocationName

        $keySetFlag = if ($StoreLocationName -eq "LocalMachine") {
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet
        } else {
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
        }

        $persistFlags = $keySetFlag -bor `
            [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::PersistKeySet

        $storeCert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
            $PfxPath,
            $PfxPassword,
            $persistFlags
        )

        $storeCert.FriendlyName = "CYBER HUB LAN HTTPS"
        $thumbprint = $storeCert.Thumbprint

        # Install certificate into the selected My store.
        $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
            [System.Security.Cryptography.X509Certificates.StoreName]::My,
            $storeEnum
        )

        $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

        try {
            $store.Add($storeCert)
        } finally {
            $store.Close()
            $store = $null
        }

        # Ensure LocalSystem service identity and Administrators
        # can read the persisted private key file.
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

                        if (Test-Path $candidate) {
                            $keyFilePath = $candidate
                        }
                    }
                }
                elseif ($privKey -is [System.Security.Cryptography.RSACryptoServiceProvider]) {
                    $uniqueName = $privKey.CspKeyContainerInfo.UniqueKeyContainerName

                    if (-not [string]::IsNullOrWhiteSpace($uniqueName)) {
                        $candidate = Join-Path "$env:ProgramData\Microsoft\Crypto\RSA\MachineKeys" $uniqueName

                        if (Test-Path $candidate) {
                            $keyFilePath = $candidate
                        }
                    }
                }

                if ($keyFilePath) {
                    Write-StepLog "Granting private key read permission for NT AUTHORITY\SYSTEM at $keyFilePath..."

                    Invoke-CheckedNative -Executable "icacls" -Arguments @(
                        $keyFilePath,
                        "/grant",
                        "NT AUTHORITY\SYSTEM:(R)",
                        "BUILTIN\Administrators:(F)"
                    )
                } else {
                    Write-StepLog "Note: Private key file path not resolved on disk; LocalSystem default machine-key access applies."
                }
            } finally {
                if ($null -ne $privKey) {
                    $privKey.Dispose()
                }
            }
        }

    } finally {
        if ($null -ne $store) {
            try { $store.Close() } catch {}
        }

        if ($null -ne $storeCert) {
            $storeCert.Dispose()
        }

        if ($null -ne $ephemeralCert) {
            $ephemeralCert.Dispose()
        }

        if ($null -ne $rsa) {
            $rsa.Dispose()
        }

        if ($null -ne $cngKey) {
            $cngKey.Dispose()
        }
    }

    # Independently validate the generated PFX.
    $verifyFlags = if ($StoreLocationName -eq "LocalMachine") {
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::MachineKeySet
    } else {
        [System.Security.Cryptography.X509Certificates.X509KeyStorageFlags]::UserKeySet
    }

    $verifyPfx = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2(
        $PfxPath,
        $PfxPassword,
        $verifyFlags
    )

    try {
        if (-not $verifyPfx.HasPrivateKey) {
            throw "PFX validation failed: certificate at $PfxPath has no private key."
        }

        $pubRsa = [System.Security.Cryptography.X509Certificates.RSACertificateExtensions]::GetRSAPublicKey($verifyPfx)

        try {
            if ($null -eq $pubRsa -or $pubRsa.KeySize -ne 2048) {
                $actualKeySize = if ($null -ne $pubRsa) { $pubRsa.KeySize } else { "Unavailable" }
                throw "PFX validation failed: expected RSA 2048-bit key, found $actualKeySize."
            }
        } finally {
            if ($null -ne $pubRsa) {
                $pubRsa.Dispose()
            }
        }

        if ($verifyPfx.SignatureAlgorithm.FriendlyName -notmatch "sha256") {
            throw "PFX validation failed: expected SHA-256 signature algorithm, found $($verifyPfx.SignatureAlgorithm.FriendlyName)."
        }

        $sanExt = $verifyPfx.Extensions |
            Where-Object { $_.Oid.Value -eq "2.5.29.17" } |
            Select-Object -First 1

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

    # Validate retrieval from target certificate store without Cert: PSDrive.
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
            if ($null -ne $storedPriv) {
                $storedPriv.Dispose()
            }
        }

    } finally {
        $verifyStore.Close()
    }

    Write-StepLog "Validated LAN HTTPS certificate in $StoreLocationName\My and PFX '$PfxPath' (Thumbprint=$thumbprint, RSA=2048, Hash=SHA256)." "Green"

    return $thumbprint
}
