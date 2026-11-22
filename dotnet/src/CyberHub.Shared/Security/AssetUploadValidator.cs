namespace CyberHub.Shared.Security;

public sealed record AssetValidationResult(bool IsValid, string MediaKind, string ContentType, string Error);

public static class AssetUploadValidator
{
    public const long MaxImageBytes = 10 * 1024 * 1024;
    public const long MaxVideoBytes = 50 * 1024 * 1024;

    private static readonly HashSet<string> AllowedImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp", ".ico"
    };

    private static readonly HashSet<string> AllowedVideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4"
    };

    public static AssetValidationResult ValidateAsset(string originalFileName, ReadOnlySpan<byte> headerBytes, long totalBytes)
    {
        if (string.IsNullOrWhiteSpace(originalFileName) || totalBytes <= 0)
        {
            return new AssetValidationResult(false, string.Empty, string.Empty, "Uploaded asset is empty.");
        }

        if (originalFileName.Contains("..", StringComparison.Ordinal) ||
            originalFileName.IndexOfAny(new[] { '/', '\\', '\0' }) >= 0)
        {
            return new AssetValidationResult(false, string.Empty, string.Empty, "Invalid asset filename.");
        }

        string ext = Path.GetExtension(originalFileName).ToLowerInvariant();

        if (AllowedImageExtensions.Contains(ext))
        {
            if (totalBytes > MaxImageBytes)
            {
                return new AssetValidationResult(false, string.Empty, string.Empty, "Image asset exceeds 10 MB limit.");
            }

            if (!MatchesImageSignature(ext, headerBytes))
            {
                return new AssetValidationResult(false, string.Empty, string.Empty, "File header does not match a valid JPG, PNG, WebP, or ICO image.");
            }

            string contentType = ext switch
            {
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".ico" => "image/x-icon",
                _ => "image/jpeg"
            };

            return new AssetValidationResult(true, "image", contentType, string.Empty);
        }

        if (AllowedVideoExtensions.Contains(ext))
        {
            if (totalBytes > MaxVideoBytes)
            {
                return new AssetValidationResult(false, string.Empty, string.Empty, "MP4 video background exceeds 50 MB limit.");
            }

            if (!MatchesMp4Signature(headerBytes))
            {
                return new AssetValidationResult(false, string.Empty, string.Empty, "File header does not match a valid MP4 video container.");
            }

            return new AssetValidationResult(true, "video", "video/mp4", string.Empty);
        }

        return new AssetValidationResult(false, string.Empty, string.Empty, $"File type '{ext}' is not permitted. Allowed: JPG, PNG, WebP, ICO, MP4.");
    }

    public static string GenerateUniqueAssetFilename(string originalFileName)
    {
        string ext = Path.GetExtension(originalFileName).ToLowerInvariant();
        if (!AllowedImageExtensions.Contains(ext) && !AllowedVideoExtensions.Contains(ext))
        {
            throw new InvalidOperationException("Unsupported extension.");
        }

        return $"asset-{Guid.NewGuid():N}{ext}";
    }

    private static bool MatchesImageSignature(string ext, ReadOnlySpan<byte> bytes)
    {
        return ext switch
        {
            ".jpg" or ".jpeg" => bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF,
            ".png" => bytes.Length >= 8 &&
                      bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47 &&
                      bytes[4] == 0x0D && bytes[5] == 0x0A && bytes[6] == 0x1A && bytes[7] == 0x0A,
            ".webp" => bytes.Length >= 12 &&
                       bytes[0] == (byte)'R' && bytes[1] == (byte)'I' && bytes[2] == (byte)'F' && bytes[3] == (byte)'F' &&
                       bytes[8] == (byte)'W' && bytes[9] == (byte)'E' && bytes[10] == (byte)'B' && bytes[11] == (byte)'P',
            ".ico" => bytes.Length >= 4 && bytes[0] == 0x00 && bytes[1] == 0x00 && bytes[2] == 0x01 && bytes[3] == 0x00,
            _ => false
        };
    }

    private static bool MatchesMp4Signature(ReadOnlySpan<byte> bytes)
    {
        // ISO Base Media File Format: "ftyp" at offset 4
        return bytes.Length >= 8 &&
               bytes[4] == (byte)'f' &&
               bytes[5] == (byte)'t' &&
               bytes[6] == (byte)'y' &&
               bytes[7] == (byte)'p';
    }
}
