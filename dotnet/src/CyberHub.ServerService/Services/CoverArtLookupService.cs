using System.Text.Json;
using CyberHub.ServerService.Configuration;
using CyberHub.Shared.Models;
using CyberHub.Shared.Security;
using CyberHub.Shared.Services;
using Microsoft.Extensions.Options;

namespace CyberHub.ServerService.Services;

public sealed class CoverArtLookupService
{
    private readonly HttpClient _httpClient;
    private readonly ServerOptions _options;
    private readonly ILogger<CoverArtLookupService> _logger;

    public CoverArtLookupService(
        HttpClient httpClient,
        IOptions<ServerOptions> options,
        ILogger<CoverArtLookupService> logger)
    {
        _httpClient = httpClient;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<CoverSearchResponse> SearchArtworkAndMetadataAsync(
        string gameTitle,
        string? externalAppId = null,
        string platform = "PC",
        CancellationToken ct = default)
    {
        var response = new CoverSearchResponse
        {
            QueryTitle = gameTitle.Trim()
        };

        if (string.IsNullOrWhiteSpace(gameTitle) && string.IsNullOrWhiteSpace(externalAppId))
        {
            return response;
        }

        // 1. Direct Steam AppID metadata lookup when AppID is known from manifest
        if (!string.IsNullOrWhiteSpace(externalAppId) && uint.TryParse(externalAppId.Trim(), out uint steamAppId))
        {
            await TryPopulateSteamAppDetailsAsync(steamAppId, response, ct);
        }

        // 2. Steam Store search by normalized title for permitted public catalog artwork & metadata
        if (!string.IsNullOrWhiteSpace(gameTitle))
        {
            await TrySearchSteamStoreAsync(gameTitle.Trim(), response, ct);
        }

        // 3. Optional RAWG official API lookup when administrator provides RAWG_API_KEY env variable
        string? rawgKey = Environment.GetEnvironmentVariable("RAWG_API_KEY");
        if (!string.IsNullOrWhiteSpace(rawgKey) && !string.IsNullOrWhiteSpace(gameTitle))
        {
            await TrySearchRawgAsync(gameTitle.Trim(), rawgKey, response, ct);
        }

        return response;
    }

    public async Task<(bool Success, string LocalRelativePath, string Error)> DownloadAndValidateCoverAsync(
        string remoteImageUrl,
        CancellationToken ct = default)
    {
        if (!UncPathValidator.IsSafeExternalArtworkUrl(remoteImageUrl))
        {
            return (false, string.Empty, "Remote artwork URL is not on the permitted metadata CDN allowlist.");
        }

        try
        {
            using HttpResponseMessage resp = await _httpClient.GetAsync(remoteImageUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return (false, string.Empty, $"Artwork download returned HTTP {(int)resp.StatusCode}.");
            }

            long? contentLen = resp.Content.Headers.ContentLength;
            if (contentLen.HasValue && contentLen.Value > _options.MaxImageUploadBytes)
            {
                return (false, string.Empty, "Artwork exceeds maximum allowed 10 MB size.");
            }

            byte[] bytes = await resp.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length == 0 || bytes.Length > _options.MaxImageUploadBytes)
            {
                return (false, string.Empty, "Artwork file is empty or exceeds size limit.");
            }

            string ext = InferImageExtension(remoteImageUrl, resp.Content.Headers.ContentType?.MediaType, bytes);
            if (string.IsNullOrEmpty(ext))
            {
                return (false, string.Empty, "Remote file is not a recognized JPG, PNG, or WebP image.");
            }

            string syntheticName = $"cover{ext}";
            AssetValidationResult validation = AssetUploadValidator.ValidateAsset(syntheticName, bytes.AsSpan(0, Math.Min(64, bytes.Length)), bytes.Length);
            if (!validation.IsValid || validation.MediaKind != "image")
            {
                return (false, string.Empty, validation.Error);
            }

            string baseDir = Path.GetFullPath(_options.CoversDirectory);
            Directory.CreateDirectory(baseDir);

            string uniqueName = AssetUploadValidator.GenerateUniqueAssetFilename(syntheticName);
            string fullPath = Path.GetFullPath(Path.Combine(baseDir, uniqueName));
            if (!fullPath.StartsWith(baseDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return (false, string.Empty, "Invalid destination path.");
            }

            await File.WriteAllBytesAsync(fullPath, bytes, ct);
            return (true, $"/covers/{uniqueName}", string.Empty);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Failed to download remote cover image {Url}", remoteImageUrl);
            return (false, string.Empty, "Could not download artwork from metadata CDN.");
        }
    }

    private async Task TryPopulateSteamAppDetailsAsync(uint appId, CoverSearchResponse target, CancellationToken ct)
    {
        try
        {
            string url = $"https://store.steampowered.com/api/appdetails?appids={appId}&l=english";
            using HttpResponseMessage resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return;
            }

            await using Stream stream = await resp.Content.ReadAsStreamAsync(ct);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty(appId.ToString(), out JsonElement appNode) ||
                !appNode.TryGetProperty("success", out JsonElement okEl) ||
                !okEl.GetBoolean() ||
                !appNode.TryGetProperty("data", out JsonElement data))
            {
                return;
            }

            string name = data.TryGetProperty("name", out JsonElement nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty;
            string shortDesc = data.TryGetProperty("short_description", out JsonElement dEl) ? (dEl.GetString() ?? string.Empty) : string.Empty;
            string headerImg = data.TryGetProperty("header_image", out JsonElement hEl) ? (hEl.GetString() ?? string.Empty) : string.Empty;

            string developer = string.Empty;
            if (data.TryGetProperty("developers", out JsonElement devsEl) && devsEl.ValueKind == JsonValueKind.Array && devsEl.GetArrayLength() > 0)
            {
                developer = devsEl[0].GetString() ?? string.Empty;
            }

            string genre = string.Empty;
            if (data.TryGetProperty("genres", out JsonElement genresEl) && genresEl.ValueKind == JsonValueKind.Array && genresEl.GetArrayLength() > 0)
            {
                genre = genresEl[0].TryGetProperty("description", out JsonElement gDesc) ? (gDesc.GetString() ?? string.Empty) : string.Empty;
            }

            string releaseYear = string.Empty;
            if (data.TryGetProperty("release_date", out JsonElement relEl) &&
                relEl.TryGetProperty("date", out JsonElement relDate) &&
                relDate.GetString() is string rawDate &&
                rawDate.Length >= 4)
            {
                foreach (string token in rawDate.Split(' ', ','))
                {
                    if (token.Length == 4 && int.TryParse(token, out int yr) && yr is >= 1980 and <= 2100)
                    {
                        releaseYear = token;
                        break;
                    }
                }
            }

            target.Metadata ??= new DetectedGameMetadata
            {
                OfficialTitle = name,
                ShortDescription = shortDesc,
                ReleaseYear = releaseYear,
                Genre = genre,
                Developer = developer,
                Platform = "PC (Steam)",
                HeroImageUrl = headerImg
            };

            string libraryPoster = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{appId}/library_600x900_2x.jpg";
            AddCandidateIfUnique(target.Candidates, new ArtworkCandidate
            {
                SourceName = "Steam Library Poster",
                Title = name,
                ImageUrl = libraryPoster,
                HeroUrl = headerImg,
                Width = 600,
                Height = 900,
                Confidence = ScanConfidence.High
            });

            if (!string.IsNullOrWhiteSpace(headerImg))
            {
                AddCandidateIfUnique(target.Candidates, new ArtworkCandidate
                {
                    SourceName = "Steam Header Artwork",
                    Title = name,
                    ImageUrl = headerImg,
                    HeroUrl = headerImg,
                    Width = 460,
                    Height = 215,
                    Confidence = ScanConfidence.Medium
                });
            }
        }
        catch (Exception)
        {
            // Non-fatal: metadata lookup optional when offline
        }
    }

    private async Task TrySearchSteamStoreAsync(string title, CoverSearchResponse target, CancellationToken ct)
    {
        try
        {
            string url = $"https://store.steampowered.com/api/storesearch/?term={Uri.EscapeDataString(title)}&l=english&cc=us";
            using HttpResponseMessage resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return;
            }

            await using Stream stream = await resp.Content.ReadAsStreamAsync(ct);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("items", out JsonElement items) || items.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            string targetNorm = GameDetectionHeuristics.NormalizeGameName(title);
            foreach (JsonElement item in items.EnumerateArray().Take(5))
            {
                if (!item.TryGetProperty("id", out JsonElement idEl) || !idEl.TryGetUInt32(out uint id))
                {
                    continue;
                }

                string itemName = item.TryGetProperty("name", out JsonElement nEl) ? (nEl.GetString() ?? title) : title;
                string tinyImg = item.TryGetProperty("tiny_image", out JsonElement tEl) ? (tEl.GetString() ?? string.Empty) : string.Empty;
                bool exact = string.Equals(GameDetectionHeuristics.NormalizeGameName(itemName), targetNorm, StringComparison.Ordinal);

                AddCandidateIfUnique(target.Candidates, new ArtworkCandidate
                {
                    SourceName = "Steam Store Catalog",
                    Title = itemName,
                    ImageUrl = $"https://shared.fastly.steamstatic.com/store_item_assets/steam/apps/{id}/library_600x900_2x.jpg",
                    HeroUrl = tinyImg,
                    Width = 600,
                    Height = 900,
                    Confidence = exact ? ScanConfidence.High : ScanConfidence.Medium
                });

                if (target.Metadata is null && exact)
                {
                    await TryPopulateSteamAppDetailsAsync(id, target, ct);
                }
            }
        }
        catch (Exception)
        {
            // Non-fatal when server has restricted outbound internet
        }
    }

    private async Task TrySearchRawgAsync(string title, string apiKey, CoverSearchResponse target, CancellationToken ct)
    {
        try
        {
            string url = $"https://api.rawg.io/api/games?key={Uri.EscapeDataString(apiKey)}&search={Uri.EscapeDataString(title)}&page_size=4";
            using HttpResponseMessage resp = await _httpClient.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode)
            {
                return;
            }

            await using Stream stream = await resp.Content.ReadAsStreamAsync(ct);
            using JsonDocument doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            if (!doc.RootElement.TryGetProperty("results", out JsonElement results) || results.ValueKind != JsonValueKind.Array)
            {
                return;
            }

            foreach (JsonElement item in results.EnumerateArray().Take(4))
            {
                string name = item.TryGetProperty("name", out JsonElement nEl) ? (nEl.GetString() ?? title) : title;
                string bgImg = item.TryGetProperty("background_image", out JsonElement bEl) ? (bEl.GetString() ?? string.Empty) : string.Empty;
                if (!string.IsNullOrWhiteSpace(bgImg))
                {
                    AddCandidateIfUnique(target.Candidates, new ArtworkCandidate
                    {
                        SourceName = "RAWG Database",
                        Title = name,
                        ImageUrl = bgImg,
                        HeroUrl = bgImg,
                        Width = 1280,
                        Height = 720,
                        Confidence = ScanConfidence.Medium
                    });
                }

                if (target.Metadata is null)
                {
                    string released = item.TryGetProperty("released", out JsonElement rEl) ? (rEl.GetString() ?? string.Empty) : string.Empty;
                    target.Metadata = new DetectedGameMetadata
                    {
                        OfficialTitle = name,
                        ReleaseYear = released.Length >= 4 ? released[..4] : string.Empty,
                        Platform = "PC",
                        HeroImageUrl = bgImg
                    };
                }
            }
        }
        catch (Exception)
        {
            // Non-fatal
        }
    }

    private static void AddCandidateIfUnique(List<ArtworkCandidate> list, ArtworkCandidate candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.ImageUrl))
        {
            return;
        }
        if (!list.Any(c => string.Equals(c.ImageUrl, candidate.ImageUrl, StringComparison.OrdinalIgnoreCase)))
        {
            list.Add(candidate);
        }
    }

    private static string InferImageExtension(string url, string? mediaType, byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
        {
            return ".png";
        }
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return ".jpg";
        }
        if (bytes.Length >= 12 && bytes[0] == 'R' && bytes[1] == 'I' && bytes[2] == 'F' && bytes[3] == 'F' &&
            bytes[8] == 'W' && bytes[9] == 'E' && bytes[10] == 'B' && bytes[11] == 'P')
        {
            return ".webp";
        }

        string pathExt = Path.GetExtension(new Uri(url).AbsolutePath).ToLowerInvariant();
        if (pathExt is ".jpg" or ".jpeg" or ".png" or ".webp")
        {
            return pathExt == ".jpeg" ? ".jpg" : pathExt;
        }

        return mediaType switch
        {
            "image/png" => ".png",
            "image/webp" => ".webp",
            "image/jpeg" => ".jpg",
            _ => string.Empty
        };
    }
}
