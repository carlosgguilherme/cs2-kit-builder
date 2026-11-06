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

    [Fact]
    public async Task MissingCatalogLocaleIsNotRequestedOnEveryLoad()
    {
        var db = Fx.TempDb();
        var stub = new StubHandler(req => req.RequestUri!.AbsolutePath.Contains("/pt-BR/", StringComparison.Ordinal)
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fx.Text("recorded/bymykel-skins-not-grouped.slice.json")) });
        var options = new DataOptions { DataDirectory = Path.Combine(Path.GetTempPath(), "kitbuilder-tests", Guid.NewGuid().ToString("N")) };
        var clock = new ManualClock(T0);
        var service = new CatalogService(new CachedDownloader(new HttpClient(stub), db, Path.Combine(options.DataDirectory, "cache"),
            clock), options);

        var first = await service.LoadItemsAsync();
        var second = await service.LoadItemsAsync();
        Assert.Equal("en", first.Locale);
        Assert.Equal("en", second.Locale);
        Assert.Equal(1, stub.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("/pt-BR/", StringComparison.Ordinal)));

        // After the check interval the locale is tried again (it may have been published).
        clock.Now = T0.AddDays(2);
        await service.LoadItemsAsync();
        Assert.Equal(2, stub.Requests.Count(r => r.RequestUri!.AbsolutePath.Contains("/pt-BR/", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task PriceOverviewVolumeIsKeptInTheCache()
    {
        var db = Fx.TempDb();
        var stub = new StubHandler(_ => StubHandler.Json(Fx.Text("recorded/steam-priceoverview-brl.2026-11-06.json")));
        var provider = new SteamMarketPriceProvider(new HttpClient(stub), db, new DataOptions(), new ManualClock(T0));
        const string name = "AK-47 | Redline (Field-Tested)";
        Assert.Equal(56, (await provider.GetAsync(name))!.Volume24h);
        Assert.Equal(56, (await provider.GetCachedAsync())[name].Volume24h);
    }

    [Fact]
    public void OlderDatabaseGetsTheVolumeColumn()
    {
        var path = Path.Combine(Path.GetTempPath(), "kitbuilder-tests", Guid.NewGuid().ToString("N"), "old.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var c = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + path))
        {
            c.Open();
            Storage.LocalDatabase.Exec(c, "CREATE TABLE prices (market_hash_name TEXT PRIMARY KEY, price_cents INTEGER NOT NULL, " +
                "currency INTEGER NOT NULL, listings INTEGER, source TEXT NOT NULL, fetched_at INTEGER NOT NULL)");
        }

        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var db = new Storage.LocalDatabase(path);
        using var conn = db.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM pragma_table_info('prices') WHERE name = 'volume_24h'";
        Assert.Equal(1L, (long)cmd.ExecuteScalar()!);
    }
}
