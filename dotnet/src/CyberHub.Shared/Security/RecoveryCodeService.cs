using System.Security.Cryptography;
using System.Text;

namespace CyberHub.Shared.Security;

public sealed class RecoveryCodeService
{
    private readonly byte[] _key;

    public RecoveryCodeService(string? configuredSecret)
    {
        if (!string.IsNullOrWhiteSpace(configuredSecret) && configuredSecret.Length >= 32)
        {
            _key = Encoding.UTF8.GetBytes(configuredSecret);
        }
        else
        {
            _key = RandomNumberGenerator.GetBytes(32);
        }
    }

    public (string Code, DateTimeOffset ExpiresAtUtc) IssueStationRecoveryCode(string stationName, TimeSpan? ttl = null)
    {
        DateTimeOffset expiresAt = DateTimeOffset.UtcNow.Add(ttl ?? TimeSpan.FromMinutes(15));
        long expUnix = expiresAt.ToUnixTimeSeconds();
        string normalizedStation = stationName.Trim().ToUpperInvariant();
        string payload = $"{normalizedStation}:{expUnix}";
        string sig = ComputeShortMac(payload);
        return ($"{expUnix}-{sig}", expiresAt);
    }

    public bool VerifyStationRecoveryCode(string stationName, string recoveryCode)
    {
        if (string.IsNullOrWhiteSpace(stationName) || string.IsNullOrWhiteSpace(recoveryCode))
        {
            return false;
        }

        string[] parts = recoveryCode.Trim().Split('-');
        if (parts.Length != 2 || !long.TryParse(parts[0], out long expUnix))
        {
            return false;
        }

        if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() > expUnix)
        {
            return false;
        }

        string normalizedStation = stationName.Trim().ToUpperInvariant();
        string expectedSig = ComputeShortMac($"{normalizedStation}:{expUnix}");

        byte[] a = Encoding.UTF8.GetBytes(parts[1].ToUpperInvariant());
        byte[] b = Encoding.UTF8.GetBytes(expectedSig.ToUpperInvariant());
        return a.Length == b.Length && CryptographicOperations.FixedTimeEquals(a, b);
    }

    private string ComputeShortMac(string data)
    {
        using var hmac = new HMACSHA256(_key);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToHexString(hash[..6]);
    }
}
