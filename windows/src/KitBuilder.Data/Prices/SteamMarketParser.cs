using System.Text.Json;

namespace KitBuilder.Data.Prices;

public sealed record SearchRow(string HashName, long SellPriceCents, int Currency, int Listings);

public sealed record SearchPage(int Start, int TotalCount, IReadOnlyList<SearchRow> Rows);

public sealed record PriceOverview(long? LowestCents, long? MedianCents, int? Volume, int Currency);

/// <summary>Parsers for Steam Community Market JSON (formats in docs/data-sources.md).</summary>
public static class SteamMarketParser
{
    /// <summary>
    /// /market/search/render/?norender=1. "sell_price" is in cents of the currency shown in
    /// "sell_price_text" (USD for anonymous requests outside Brazil; the "currency" parameter is ignored).
    /// </summary>
    public static SearchPage ParseSearch(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            throw new FormatException("Steam search response without success=true.");
        }

        var rows = new List<SearchRow>();
        foreach (var r in root.GetProperty("results").EnumerateArray())
        {
            var text = r.TryGetProperty("sell_price_text", out var t) ? t.GetString() : null;
            SteamMoney.TryParse(text, out _, out var currency);
            rows.Add(new SearchRow(
                r.GetProperty("hash_name").GetString() ?? "",
                r.GetProperty("sell_price").GetInt64(),
                currency == 0 ? SteamCurrency.Usd : currency,
                r.TryGetProperty("sell_listings", out var l) ? l.GetInt32() : 0));
        }

        return new SearchPage(root.GetProperty("start").GetInt32(), root.GetProperty("total_count").GetInt32(), rows);
    }

    /// <summary>/market/priceoverview/ — strings like "R$ 12,34"; volume like "1,234".</summary>
    public static PriceOverview? ParseOverview(string json, int requestedCurrency)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("success", out var ok) || ok.ValueKind != JsonValueKind.True)
        {
            return null;
        }

        long? Money(string name)
        {
            if (root.TryGetProperty(name, out var v) && SteamMoney.TryParse(v.GetString(), out var c, out _))
            {
                return c;
            }

            return null;
        }

        int? volume = null;
        if (root.TryGetProperty("volume", out var vol) && int.TryParse((vol.GetString() ?? "").Replace(",", "").Replace(".", ""),
                out var vv))
        {
            volume = vv;
        }

        return new PriceOverview(Money("lowest_price"), Money("median_price"), volume, requestedCurrency);
    }
}
