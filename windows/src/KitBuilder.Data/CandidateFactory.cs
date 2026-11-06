using KitBuilder.Core.Color;
using KitBuilder.Core.Kits;
using KitBuilder.Data.Catalog;
using KitBuilder.Data.Inventory;
using KitBuilder.Data.Prices;

namespace KitBuilder.Data;

/// <summary>Joins catalog, prices, color index and (optionally) the user's inventory into engine candidates.</summary>
public static class CandidateFactory
{
    public static List<KitCandidate> Build(
        IReadOnlyList<CatalogItem> catalog,
        IReadOnlyDictionary<string, PriceQuote> prices,
        IReadOnlyDictionary<string, SkinColorProfile> colors,
        IReadOnlyList<InventoryItem>? owned = null)
    {
        var list = new List<KitCandidate>(catalog.Count);
        var byName = new Dictionary<string, CatalogItem>(StringComparer.Ordinal);
        foreach (var item in catalog)
        {
            byName.TryAdd(item.MarketHashName, item);
            if (!prices.TryGetValue(item.MarketHashName, out var price))
            {
                continue;
            }

            var (fmin, fmax) = CatalogParser.VariantFloatRange(item);
            list.Add(new KitCandidate(item.ItemId, item.SkinId, item.Name, item.Weapon, item.Category, item.Rarity,
                item.Exterior, item.StatTrak, item.Souvenir, price.PriceCents, colors.GetValueOrDefault(item.SkinId),
                price.Volume24h, fmin, fmax));
        }

        foreach (var inv in owned ?? [])
        {
            // Doppler phases share a market name: the first catalog entry is used for colors.
            if (!byName.TryGetValue(inv.MarketHashName, out var item))
            {
                continue;
            }

            long price = prices.TryGetValue(inv.MarketHashName, out var q) ? q.PriceCents : 0;
            list.Add(new KitCandidate("owned:" + inv.AssetId, item.SkinId, item.Name, item.Weapon, item.Category,
                item.Rarity, item.Exterior, item.StatTrak, item.Souvenir, price, colors.GetValueOrDefault(item.SkinId),
                q?.Volume24h, inv.Float ?? CatalogParser.VariantFloatRange(item).Min,
                inv.Float ?? CatalogParser.VariantFloatRange(item).Max, Owned: true));
        }

        return list;
    }
}
