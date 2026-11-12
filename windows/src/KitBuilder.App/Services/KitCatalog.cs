using KitBuilder.Core.Color;
using KitBuilder.Core.Kits;
using KitBuilder.Data;
using KitBuilder.Data.Catalog;
using KitBuilder.Data.Prices;
using KitBuilder.Presentation;

namespace KitBuilder.App.Services;

/// <summary>Candidates for the engine from the cached catalog, color index and Steam prices.</summary>
public sealed class KitCatalog(CatalogService catalogService, IPriceProvider prices) : IKitCatalog
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyList<CatalogItem>? items;
    private IReadOnlyDictionary<string, SkinColorProfile>? colors;
    private Dictionary<string, CatalogItem> byItemId = new(StringComparer.Ordinal);
    private Dictionary<string, CatalogItem> byName = new(StringComparer.Ordinal);

    public async Task<IReadOnlyList<KitCandidate>> GetCandidatesAsync(CancellationToken ct = default)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (items is null)
            {
                // The catalog is a 32 MB JSON file: load it once, off the UI thread.
                var loaded = await Task.Run(() => catalogService.LoadItemsAsync(ct), ct);
                colors = await Task.Run(() => catalogService.LoadColorIndexAsync(ct), ct);
                items = loaded.Items;
                byItemId = items.ToDictionary(i => i.ItemId, StringComparer.Ordinal);
                byName = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
                foreach (var i in items)
                {
                    byName.TryAdd(i.Name, i);
                }
            }
        }
        finally
        {
            gate.Release();
        }

        // Prices change while the background refresh runs, so they are read every time.
        var quotes = await prices.GetCachedAsync(ct);
        return await Task.Run(() => CandidateFactory.Build(items!, quotes, colors!), ct);
    }

    public ItemVisual GetVisual(KitCandidate candidate)
    {
        CatalogItem? item = byItemId.GetValueOrDefault(candidate.ItemId) ?? byName.GetValueOrDefault(candidate.Name);
        string market = "https://steamcommunity.com/market/listings/730/" + Uri.EscapeDataString(item?.MarketHashName ?? candidate.Name);
        return new ItemVisual(item?.ImageUrl is { } url ? url + "/256fx256f" : null, item?.RarityColor ?? "#b0c3d9", market);
    }
}
