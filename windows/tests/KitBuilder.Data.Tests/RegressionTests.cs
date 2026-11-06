using System.Net;
using KitBuilder.Data.Catalog;
using KitBuilder.Data.Http;
using KitBuilder.Data.Prices;

namespace KitBuilder.Data.Tests;

/// <summary>Regression tests for bugs tracked as GitHub issues.</summary>
public class RegressionTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void DopplerGemPhasesAreNotPricedWithTheSharedListing()
    {
        // Steam lists every Doppler phase under one market_hash_name, so its lowest price is the
        // cheapest phase's (~R$ 6,000 for a FN Karambit) while Ruby/Sapphire/Black Pearl cost
        // R$ 24,000-45,000. Offering a gem at the shared price would be wrong.
        using var fs = File.OpenRead(Fx.Path("recorded/bymykel-skins-not-grouped.slice.json"));
        var catalog = CatalogParser.ParseSkinsNotGrouped(fs);
        const string name = "★ Karambit | Doppler (Factory New)";
        var prices = new Dictionary<string, PriceQuote> { [name] = new(name, 600000, 7, 40, null, "steam", T0, false) };
        var cands = CandidateFactory.Build(catalog, prices, new Dictionary<string, Core.Color.SkinColorProfile>());
        var phases = cands.Where(c => c.Name == name).Select(c => catalog.Single(i => i.ItemId == c.ItemId).Phase).ToList();
        Assert.Contains("Phase 1", phases);
        Assert.DoesNotContain("Black Pearl", phases);
    }
}
