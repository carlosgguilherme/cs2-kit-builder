using System.Text.Json;
using KitBuilder.Data.Storage;

namespace KitBuilder.Data.Catalog;

/// <summary>
/// Finds the color index published by the weekly data workflow: the newest non-draft
/// release tagged <c>data-YYYYMMDD</c> that has a <c>color-index.json</c> asset. Uses the
/// public GitHub REST API (no token), at most once per catalog check interval, and keeps
/// the last known URL when offline.
/// </summary>
public sealed class DataReleaseLocator(HttpClient http, LocalDatabase db, DataOptions options, TimeProvider? time = null)
{
    public const string AssetName = "color-index.json";
    private const string CacheKey = "color_index_url";
    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public string ReleasesUrl => $"https://api.github.com/repos/{options.DataRepository}/releases?per_page=30";

    public async Task<string?> GetColorIndexUrlAsync(CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        Cached? cached = db.GetValue(CacheKey) is { } raw ? JsonSerializer.Deserialize<Cached>(raw) : null;
        if (cached is not null && now - DateTimeOffset.FromUnixTimeSeconds(cached.CheckedAt) < options.CatalogCheckInterval)
        {
            return cached.Url;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            req.Headers.TryAddWithoutValidation("Accept", "application/vnd.github+json");
            using var resp = await http.SendAsync(req, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            var url = ParseColorIndexUrl(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false)) ?? cached?.Url;
            if (url is not null)
            {
                db.SetValue(CacheKey, JsonSerializer.Serialize(new Cached(url, now.ToUnixTimeSeconds())), now);
            }

            return url;
        }
        catch (Exception) when (!ct.IsCancellationRequested)
        {
            return cached?.Url; // offline or API limit: last known release
        }
    }

    /// <summary>Newest (by tag date) non-draft <c>data-*</c> release that contains the asset.</summary>
    public static string? ParseColorIndexUrl(string releasesJson)
    {
        using var doc = JsonDocument.Parse(releasesJson);
        string? bestTag = null, bestUrl = null;
        foreach (var r in doc.RootElement.EnumerateArray())
        {
            var tag = r.GetProperty("tag_name").GetString() ?? "";
            if (!tag.StartsWith("data-", StringComparison.Ordinal) || r.GetProperty("draft").GetBoolean())
            {
                continue;
            }

            foreach (var a in r.GetProperty("assets").EnumerateArray())
            {
                if (a.GetProperty("name").GetString() == AssetName
                    && (bestTag is null || string.CompareOrdinal(tag, bestTag) > 0))
                {
                    bestTag = tag;
                    bestUrl = a.GetProperty("browser_download_url").GetString();
                }
            }
        }

        return bestUrl;
    }

    private sealed record Cached(string Url, long CheckedAt);
}
