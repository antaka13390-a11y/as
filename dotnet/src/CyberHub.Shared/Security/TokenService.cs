using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CyberHub.Shared.Security;

public sealed class TokenPayload
{
    public string Subject { get; set; } = string.Empty;
    public string Workstation { get; set; } = string.Empty;
    public string SourceIp { get; set; } = string.Empty;
    public long ExpiresUnixSeconds { get; set; }
    public long TokenEpoch { get; set; }
    public string Nonce { get; set; } = string.Empty;
}

public sealed class TokenService
{
    private readonly byte[] _signingKey;
    private long _minimumValidEpoch;

    public TokenService(string? configuredSecret, long initialEpoch = 1)
    {
        if (!string.IsNullOrWhiteSpace(configuredSecret) && configuredSecret.Length >= 32)
        {
            _signingKey = Encoding.UTF8.GetBytes(configuredSecret);
        }
        else
        {
            _signingKey = RandomNumberGenerator.GetBytes(32);
        }

        _minimumValidEpoch = Math.Max(1, initialEpoch);
    }

    public long CurrentEpoch => Volatile.Read(ref _minimumValidEpoch);

    public void SetMinimumValidEpoch(long epoch)
    {
        Volatile.Write(ref _minimumValidEpoch, Math.Max(1, epoch));
    }

    public long RevokeAllTokens()
    {
        return Interlocked.Increment(ref _minimumValidEpoch);
    }

    public string IssueToken(string subject, string workstation, string sourceIp, TimeSpan lifetime)
    {
        var payload = new TokenPayload
        {
            Subject = subject,
            Workstation = workstation,
            SourceIp = sourceIp,
            ExpiresUnixSeconds = DateTimeOffset.UtcNow.Add(lifetime).ToUnixTimeSeconds(),
            TokenEpoch = CurrentEpoch,
            Nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(8))
        };

        string payloadJson = JsonSerializer.Serialize(payload);
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        byte[] sigBytes = ComputeHmac(payloadB64);
        string sigB64 = Base64UrlEncode(sigBytes);

        return $"{payloadB64}.{sigB64}";
    }

    public bool ValidateToken(string token, string expectedSourceIp, out TokenPayload? payload)
    {
        payload = null;
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        string[] parts = token.Split('.');
        if (parts.Length != 2)
        {
            return false;
        }

        byte[] expectedSig = ComputeHmac(parts[0]);
        byte[] providedSig;
        try
        {
            providedSig = Base64UrlDecode(parts[1]);
        }
        catch (FormatException)
        {
            return false;
        }

        if (!CryptographicOperations.FixedTimeEquals(expectedSig, providedSig))
        {
            return false;
        }

        try
        {
            string json = Encoding.UTF8.GetString(Base64UrlDecode(parts[0]));
            TokenPayload? parsed = JsonSerializer.Deserialize<TokenPayload>(json);
            if (parsed is null)
            {
                return false;
            }

            if (parsed.TokenEpoch < CurrentEpoch)
            {
                return false;
            }

            if (DateTimeOffset.UtcNow.ToUnixTimeSeconds() >= parsed.ExpiresUnixSeconds)
            {
                return false;
            }

            if (!string.Equals(parsed.SourceIp, expectedSourceIp, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            payload = parsed;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private byte[] ComputeHmac(string data)
    {
        using var hmac = new HMACSHA256(_signingKey);
        return hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    private static string Base64UrlEncode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Base64UrlDecode(string input)
    {
        string padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
