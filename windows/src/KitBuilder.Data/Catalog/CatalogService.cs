using System.Net;
using KitBuilder.Data.Colors;
using KitBuilder.Data.Http;
using KitBuilder.Core.Color;

namespace KitBuilder.Data.Catalog;

/// <summary>Loads the item catalog (ByMykel) and the color index (data release), both cached.</summary>
public sealed class CatalogService(CachedDownloader downloader, DataOptions options)
{
    /// <summary>Color index published by the data pipeline (release asset URL or a local file path).</summary>
    public string? ColorIndexUrl { get; set; }

    public async Task<(IReadOnlyList<CatalogItem> Items, DateTimeOffset CheckedAt, string Locale)> LoadItemsAsync(CancellationToken ct = default)
    {
        foreach (var locale in options.CatalogLocales)
        {
            var url = options.CatalogBaseUrl + locale + "/skins_not_grouped.json";
            try
            {
                var r = await downloader.GetAsync(url, options.CatalogCheckInterval, ct).ConfigureAwait(false);
                await using var fs = File.OpenRead(r.FilePath);
                return (CatalogParser.ParseSkinsNotGrouped(fs), r.CheckedAt, locale);
            }
            catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound)
            {
                // Locale not published (e.g. pt-BR today): fall back to the next one.
            }
        }

        throw new InvalidOperationException("No catalog locale could be loaded.");
    }

    public async Task<IReadOnlyDictionary<string, SkinColorProfile>> LoadColorIndexAsync(CancellationToken ct = default)
    {
        if (ColorIndexUrl is null)
        {
            return new Dictionary<string, SkinColorProfile>();
        }

        // A local path is accepted for development (before a data release exists).
        var path = File.Exists(ColorIndexUrl)
            ? ColorIndexUrl
            : (await downloader.GetAsync(ColorIndexUrl, options.CatalogCheckInterval, ct).ConfigureAwait(false)).FilePath;
        await using var fs = File.OpenRead(path);
        return ColorIndexParser.Parse(fs);
    }
}
