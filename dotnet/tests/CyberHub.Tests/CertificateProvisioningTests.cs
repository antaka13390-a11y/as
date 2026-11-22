using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Xunit;

namespace CyberHub.Tests;

public sealed class CertificateProvisioningTests : IDisposable
{
    private readonly string _tempDir;
    private readonly List<(StoreLocation Location, string Thumbprint)> _createdCerts = new();

    public CertificateProvisioningTests()
    {
        _tempDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CyberHubCertTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    [Fact]
    public void CertificateRequest_CreatesValidRsa2048Sha256Pfx_WithDnsSan()
    {
        string pfxPath = Path.Combine(_tempDir, "test-dotnet.pfx");
        const string pfxPassword = "TestCertPassword!2026";

        using RSA rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=ccboot-test.lan",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);

        var sanBuilder = new SubjectAlternativeNameBuilder();
        sanBuilder.AddDnsName("ccboot-test.lan");
        sanBuilder.AddDnsName("localhost");
        request.CertificateExtensions.Add(sanBuilder.Build());
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, false));
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));

        using X509Certificate2 cert = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow.AddYears(2));

        byte[] pfxBytes = cert.Export(X509ContentType.Pfx, pfxPassword);
        File.WriteAllBytes(pfxPath, pfxBytes);

        using var loaded = new X509Certificate2(pfxPath, pfxPassword, X509KeyStorageFlags.Exportable);
        Assert.True(loaded.HasPrivateKey);
        using RSA? publicRsa = loaded.GetRSAPublicKey();
        Assert.NotNull(publicRsa);
        Assert.Equal(2048, publicRsa!.KeySize);
        Assert.Contains("sha256", loaded.SignatureAlgorithm.FriendlyName ?? string.Empty, StringComparison.OrdinalIgnoreCase);

        X509Extension? sanExt = loaded.Extensions["2.5.29.17"];
        Assert.NotNull(sanExt);
        string formattedSan = sanExt!.Format(false);
        Assert.Contains("ccboot-test.lan", formattedSan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("localhost", formattedSan, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InstallCyberHubServerScript_CreatesAndRetrievesCertInPowerShell51_WithoutCertPsDrive()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        string scriptPath = FindServerInstallScriptPath();
        string pfxPath = Path.Combine(_tempDir, "ps51-server-test.pfx");
        string psExe = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.System),
            @"WindowsPowerShell\v1.0\powershell.exe");
        Assert.True(File.Exists(psExe), $"Expected Windows PowerShell 5.1 at {psExe}");

        var psi = new ProcessStartInfo
        {
            FileName = psExe,
            Arguments = $"-NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass -File \"{scriptPath}\" -CertificateOnly -CertificateDnsName \"ccboot-test.lan\" -OutputPfxPath \"{pfxPath}\" -CertStoreLocation \"CurrentUser\"",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using Process proc = Process.Start(psi) ?? throw new InvalidOperationException("Failed to start powershell.exe");
        string stdout = proc.StandardOutput.ReadToEnd();
        string stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        Assert.True(
            proc.ExitCode == 0,
            $"powershell.exe failed with exit code {proc.ExitCode}.\nSTDOUT:\n{stdout}\nSTDERR:\n{stderr}");
        Assert.True(File.Exists(pfxPath), $"Expected PFX file at {pfxPath}");

        string? thumbLine = stdout
            .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
            .FirstOrDefault(line => line.StartsWith("CYBERHUB_CERT_THUMBPRINT=", StringComparison.Ordinal));
        Assert.False(string.IsNullOrWhiteSpace(thumbLine), $"Missing CYBERHUB_CERT_THUMBPRINT in output:\n{stdout}");

        string thumbprint = thumbLine!.Substring("CYBERHUB_CERT_THUMBPRINT=".Length).Trim();
        _createdCerts.Add((StoreLocation.CurrentUser, thumbprint));

        using var store = new X509Store(StoreName.My, StoreLocation.CurrentUser);
        store.Open(OpenFlags.ReadOnly);
        try
        {
            X509Certificate2Collection matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
            Assert.True(matches.Count >= 1, $"Certificate {thumbprint} not found in CurrentUser\\My.");

            X509Certificate2 stored = matches[0];
            Assert.True(stored.HasPrivateKey);
            using RSA? pubRsa = stored.GetRSAPublicKey();
            Assert.NotNull(pubRsa);
            Assert.Equal(2048, pubRsa!.KeySize);
            using RSA? privRsa = stored.GetRSAPrivateKey();
            Assert.NotNull(privRsa);
            Assert.Contains("sha256", stored.SignatureAlgorithm.FriendlyName ?? string.Empty, StringComparison.OrdinalIgnoreCase);

            X509Extension? sanExt = stored.Extensions["2.5.29.17"];
            Assert.NotNull(sanExt);
            string sanText = sanExt!.Format(false);
            Assert.Contains("ccboot-test.lan", sanText, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("localhost", sanText, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            store.Close();
        }
    }

    private static string FindServerInstallScriptPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, "scripts", "Install-CyberHubServer.ps1");
            if (File.Exists(candidate))
            {
                return candidate;
            }
            string nested = Path.Combine(dir.FullName, "dotnet", "scripts", "Install-CyberHubServer.ps1");
            if (File.Exists(nested))
            {
                return nested;
            }
            dir = dir.Parent;
        }

        throw new FileNotFoundException("Could not locate scripts/Install-CyberHubServer.ps1 from test output directory.");
    }

    public void Dispose()
    {
        if (OperatingSystem.IsWindows())
        {
            foreach ((StoreLocation location, string thumbprint) in _createdCerts)
            {
                try
                {
                    using var store = new X509Store(StoreName.My, location);
                    store.Open(OpenFlags.ReadWrite);
                    X509Certificate2Collection matches = store.Certificates.Find(X509FindType.FindByThumbprint, thumbprint, validOnly: false);
                    foreach (X509Certificate2 cert in matches)
                    {
                        store.Remove(cert);
                    }
                    store.Close();
                }
                catch
                {
                }
            }
        }

        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
        }
    }
}
