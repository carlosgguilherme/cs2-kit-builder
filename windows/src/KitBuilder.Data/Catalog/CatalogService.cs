using System.Net;
using KitBuilder.Data.Colors;
using KitBuilder.Data.Http;
using KitBuilder.Core.Color;

namespace KitBuilder.Data.Catalog;

/// <summary>Loads the item catalog (ByMykel) and the color index (data release), both cached.</summary>
public sealed class CatalogService(CachedDownloader downloader, DataOptions options, DataReleaseLocator? locator = null)
{
    /// <summary>
    /// Color index location (URL or local file path). When null, the newest data release is
    /// located through <see cref="DataReleaseLocator"/>.
    /// </summary>
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
        var source = ColorIndexUrl ?? (locator is null ? null : await locator.GetColorIndexUrlAsync(ct).ConfigureAwait(false));
        if (source is null)
        {
            return new Dictionary<string, SkinColorProfile>();
        }

        // A local path is accepted for development.
        var path = File.Exists(source)
            ? source
            : (await downloader.GetAsync(source, options.CatalogCheckInterval, ct).ConfigureAwait(false)).FilePath;
        await using var fs = File.OpenRead(path);
        return ColorIndexParser.Parse(fs);
    }
}
