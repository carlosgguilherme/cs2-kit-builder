using System.Globalization;
using System.Text.Json;
using KitBuilder.Data.Http;
using KitBuilder.Data.Storage;

namespace KitBuilder.Data.Prices;

/// <summary>
/// Steam Community Market prices. Bulk: paginated search (10 rows per page, resumable crawl).
/// Single item: priceoverview in the display currency. USD rows are converted with
/// the USD->BRL rate implied by Steam's own priceoverview prices (see <see cref="FxRateFromSteam"/>).
/// </summary>
public sealed class SteamMarketPriceProvider(HttpClient http, LocalDatabase db, DataOptions options, TimeProvider? time = null)
    : IPriceProvider
{
    private const string CrawlKey = "steam_crawl";
    private const string FxKey = "fx_usd_to_display";
    private static readonly string[] Types =
    [
        "tag_CSGO_Type_Pistol", "tag_CSGO_Type_SMG", "tag_CSGO_Type_Rifle", "tag_CSGO_Type_SniperRifle",
        "tag_CSGO_Type_Shotgun", "tag_CSGO_Type_Machinegun", "tag_CSGO_Type_Knife", "tag_Type_Hands",
    ];

    /// <summary>Liquid items used to derive Steam's USD -> display currency rate.</summary>
    public static readonly string[] FxReferenceItems =
    [
        "AK-47 | Redline (Field-Tested)", "AWP | Asiimov (Field-Tested)", "Glock-18 | Water Elemental (Field-Tested)",
    ];

    private readonly TimeProvider clock = time ?? TimeProvider.System;

    public string Name => "steam";

    public static string SearchUrl(int start) =>
        "https://steamcommunity.com/market/search/render/?appid=730&norender=1&count=10&sort_column=name&sort_dir=asc"
        + string.Concat(Types.Select(t => "&category_730_Type%5B%5D=" + t)) + "&start=" + start.ToString(CultureInfo.InvariantCulture);

    public static string OverviewUrl(string marketHashName, int currency) =>
        "https://steamcommunity.com/market/priceoverview/?appid=730&currency=" + currency.ToString(CultureInfo.InvariantCulture)
        + "&market_hash_name=" + Uri.EscapeDataString(marketHashName);

    public async Task<IReadOnlyDictionary<string, PriceQuote>> GetCachedAsync(CancellationToken ct = default)
    {
        var fx = FxRate();
        var now = clock.GetUtcNow();
        var map = new Dictionary<string, PriceQuote>(StringComparer.Ordinal);
        await Task.Yield();
        using var c = db.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT market_hash_name, price_cents, currency, listings, source, fetched_at, volume_24h FROM prices";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var q = ToQuote(r.GetString(0), r.GetInt64(1), r.GetInt32(2), r.IsDBNull(3) ? null : r.GetInt32(3),
                r.IsDBNull(6) ? null : r.GetInt32(6),
                r.GetString(4), DateTimeOffset.FromUnixTimeSeconds(r.GetInt64(5)), fx, now);
            if (q is not null)
            {
                map[q.MarketHashName] = q;
            }
        }

        return map;
    }

    public async Task<PriceQuote?> GetAsync(string marketHashName, CancellationToken ct = default)
    {
        var cached = (await GetCachedAsync(ct).ConfigureAwait(false)).GetValueOrDefault(marketHashName);
        if (cached is { IsStale: false })
        {
            return cached;
        }

        var json = await http.GetStringAsync(OverviewUrl(marketHashName, options.SteamCurrency), ct).ConfigureAwait(false);
        var ov = SteamMarketParser.ParseOverview(json, options.SteamCurrency);
        if (ov?.LowestCents is not { } lowest)
        {
            return cached;
        }

        var now = clock.GetUtcNow();
        Upsert([(marketHashName, lowest, options.SteamCurrency, (int?)null, ov.Volume)], "steam_priceoverview", now);
        return new PriceQuote(marketHashName, lowest, options.SteamCurrency, null, ov.Volume, "steam_priceoverview", now, false);
    }

    public async Task RefreshAllAsync(IProgress<RefreshProgress>? progress = null, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        var state = LoadCrawl();
        if (state.CompletedAt is { } done && now - done < options.PriceTtl)
        {
            return; // fresh enough
        }

        if (state.CompletedAt is not null || now - state.StartedAt > TimeSpan.FromDays(1))
        {
            state = new CrawlState(0, 0, now, null);
        }

        await EnsureFxRateAsync(ct).ConfigureAwait(false);
        while (state.Total == 0 || state.Start < state.Total)
        {
            ct.ThrowIfCancellationRequested();
            SearchPage page;
            try
            {
                page = SteamMarketParser.ParseSearch(await http.GetStringAsync(SearchUrl(state.Start), ct).ConfigureAwait(false));
            }
            catch (RateLimitedException e)
            {
                progress?.Report(new RefreshProgress(state.Start, state.Total, e.RetryAfter));
                throw;
            }

            Upsert(page.Rows.Select(r => (r.HashName, r.SellPriceCents, r.Currency, (int?)r.Listings, (int?)null)), "steam_search",
                clock.GetUtcNow());
            state = state with { Start = state.Start + 10, Total = page.TotalCount };
            SaveCrawl(state);
            progress?.Report(new RefreshProgress(Math.Min(state.Start, state.Total), state.Total, null));
            if (page.Rows.Count == 0)
            {
                break;
            }
        }

        SaveCrawl(state with { CompletedAt = clock.GetUtcNow() });
    }

    /// <summary>
    /// Median of (display-currency price / USD price) over liquid reference items, both
    /// from Steam's priceoverview. Cached for 24 h; the last known value is kept offline.
    /// </summary>
    public async Task<double?> EnsureFxRateAsync(CancellationToken ct = default)
    {
        if (options.SteamCurrency == SteamCurrency.Usd)
        {
            return 1.0;
        }

        var raw = db.GetValue(FxKey);
        if (raw is not null)
        {
            var saved = JsonSerializer.Deserialize<FxState>(raw)!;
            if (clock.GetUtcNow() - DateTimeOffset.FromUnixTimeSeconds(saved.At) < TimeSpan.FromDays(1))
            {
                return saved.Rate;
            }
        }

        var ratios = new List<double>();
        foreach (var item in FxReferenceItems)
        {
            try
            {
                var local = SteamMarketParser.ParseOverview(
                    await http.GetStringAsync(OverviewUrl(item, options.SteamCurrency), ct).ConfigureAwait(false), options.SteamCurrency);
                var usd = SteamMarketParser.ParseOverview(
                    await http.GetStringAsync(OverviewUrl(item, SteamCurrency.Usd), ct).ConfigureAwait(false), SteamCurrency.Usd);
                if (FxRateFromSteam(local, usd) is { } ratio)
                {
                    ratios.Add(ratio);
                }
            }
            catch (HttpRequestException)
            {
                // Try the next reference item.
            }
        }

        if (ratios.Count == 0)
        {
            return FxRate();
        }

        ratios.Sort();
        double rate = ratios[ratios.Count / 2];
        db.SetValue(FxKey, JsonSerializer.Serialize(new FxState(rate, clock.GetUtcNow().ToUnixTimeSeconds())), clock.GetUtcNow());
        return rate;
    }

    /// <summary>Rate from a pair of priceoverview answers (median price preferred over lowest).</summary>
    public static double? FxRateFromSteam(PriceOverview? local, PriceOverview? usd)
    {
        long? l = local?.MedianCents ?? local?.LowestCents;
        long? u = usd?.MedianCents ?? usd?.LowestCents;
        return l is > 0 && u is > 0 ? (double)l.Value / u.Value : null;
    }

    private double? FxRate()
    {
        if (options.SteamCurrency == SteamCurrency.Usd)
        {
            return 1.0;
        }

        var raw = db.GetValue(FxKey);
        return raw is null ? null : JsonSerializer.Deserialize<FxState>(raw)!.Rate;
    }

    private PriceQuote? ToQuote(string name, long cents, int currency, int? listings, int? volume, string source,
        DateTimeOffset fetchedAt, double? fx, DateTimeOffset now)
    {
        long display;
        if (currency == options.SteamCurrency)
        {
            display = cents;
        }
        else if (currency == SteamCurrency.Usd && fx is { } rate)
        {
            display = (long)Math.Round(cents * rate);
        }
        else
        {
            return null; // cannot convert yet
        }

        return new PriceQuote(name, display, options.SteamCurrency, listings, volume, source, fetchedAt,
            now - fetchedAt > options.PriceTtl);
    }

    /// <summary>Null listings/volume keep the stored value (search rows have no volume, priceoverview has no listings).</summary>
    private void Upsert(IEnumerable<(string Name, long Cents, int Currency, int? Listings, int? Volume)> rows, string source,
        DateTimeOffset at)
    {
        using var c = db.Open();
        using var tx = c.BeginTransaction();
        foreach (var (name, cents, currency, listings, volume) in rows)
        {
            if (cents <= 0)
            {
                continue; // no listing
            }

            LocalDatabase.Exec(c, """
                INSERT INTO prices(market_hash_name, price_cents, currency, listings, source, fetched_at, volume_24h)
                VALUES ($n, $p, $c, $l, $s, $t, $v)
                ON CONFLICT(market_hash_name) DO UPDATE SET price_cents = $p, currency = $c,
                  listings = COALESCE($l, listings), source = $s, fetched_at = $t, volume_24h = COALESCE($v, volume_24h)
                """, ("$n", name), ("$p", cents), ("$c", currency), ("$l", listings), ("$s", source), ("$t", at.ToUnixTimeSeconds()),
                ("$v", volume));
        }

        tx.Commit();
    }

    private sealed record CrawlState(int Start, int Total, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);

    private sealed record FxState(double Rate, long At);

    private CrawlState LoadCrawl()
    {
        var raw = db.GetValue(CrawlKey);
        return raw is null ? new CrawlState(0, 0, clock.GetUtcNow(), null) : JsonSerializer.Deserialize<CrawlState>(raw)!;
    }

    private void SaveCrawl(CrawlState s) => db.SetValue(CrawlKey, JsonSerializer.Serialize(s), clock.GetUtcNow());
}
