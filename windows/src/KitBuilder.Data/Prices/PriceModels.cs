namespace KitBuilder.Data.Prices;

/// <summary>Steam currency codes used by the app.</summary>
public static class SteamCurrency
{
    public const int Usd = 1;
    public const int Brl = 7;
}

/// <summary>A price in the display currency (cents), with its age.</summary>
public sealed record PriceQuote(
    string MarketHashName,
    long PriceCents,
    int Currency,
    int? Listings,
    int? Volume24h,
    string Source,
    DateTimeOffset FetchedAt,
    bool IsStale);

public sealed record RefreshProgress(int Done, int Total, DateTimeOffset? RetryAfter);

/// <summary>Price source abstraction (Steam today; other providers pluggable, off by default).</summary>
public interface IPriceProvider
{
    string Name { get; }

    /// <summary>All cached prices converted to the display currency (stale ones flagged).</summary>
    Task<IReadOnlyDictionary<string, PriceQuote>> GetCachedAsync(CancellationToken ct = default);

    /// <summary>Price of one item: cache when fresh, otherwise a live request.</summary>
    Task<PriceQuote?> GetAsync(string marketHashName, CancellationToken ct = default);

    /// <summary>Incremental bulk refresh (resumable); safe to run in the background.</summary>
    Task RefreshAllAsync(IProgress<RefreshProgress>? progress = null, CancellationToken ct = default);
}
