using System.Net;
using KitBuilder.Core.Color;
using KitBuilder.Data.Catalog;
using KitBuilder.Data.Http;
using KitBuilder.Data.Inventory;
using KitBuilder.Data.Prices;

namespace KitBuilder.Data.Tests;

public class ClientTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ThrottlerRetriesAfter429ThenGivesUp()
    {
        SteamThrottlingHandler.Reset();
        var backoff = TimeSpan.FromMilliseconds(5);
        int calls = 0;
        var stub = new StubHandler(_ => ++calls < 3 ? new HttpResponseMessage(HttpStatusCode.TooManyRequests) : StubHandler.Json("{}"));
        using var http = new HttpClient(new SteamThrottlingHandler(TimeSpan.Zero, maxRetries: 3, initialBackoff: backoff) { InnerHandler = stub });
        Assert.Equal("{}", await http.GetStringAsync("https://steamcommunity.com/x"));
        Assert.Equal(3, calls);

        var always429 = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));
        using var http2 = new HttpClient(new SteamThrottlingHandler(TimeSpan.Zero, maxRetries: 1, initialBackoff: backoff) { InnerHandler = always429 });
        await Assert.ThrowsAsync<RateLimitedException>(() => http2.GetStringAsync("https://steamcommunity.com/y"));
        SteamThrottlingHandler.Reset();
    }

    [Fact]
    public async Task CachedDownloaderRevalidatesWithEtagAndWorksOffline()
    {
        var db = Fx.TempDb();
        var clock = new ManualClock(T0);
        int mode = 0; // 0 = 200 with ETag, 1 = expect 304, 2 = offline
        var stub = new StubHandler(req => mode switch
        {
            0 => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[1]"),
                Headers = { ETag = new System.Net.Http.Headers.EntityTagHeaderValue("\"v1\"") },
            },
            1 when req.Headers.IfNoneMatch.ToString() == "\"v1\"" => new HttpResponseMessage(HttpStatusCode.NotModified),
            _ => throw new HttpRequestException("offline"),
        });
        var dl = new CachedDownloader(new HttpClient(stub), db, Path.Combine(Path.GetTempPath(), "kitbuilder-tests", Guid.NewGuid().ToString("N")), clock);

        var first = await dl.GetAsync("https://example.invalid/skins.json", TimeSpan.FromDays(1));
        Assert.True(first.FromNetwork);
        Assert.Equal("[1]", File.ReadAllText(first.FilePath));

        var cached = await dl.GetAsync("https://example.invalid/skins.json", TimeSpan.FromDays(1));
        Assert.False(cached.FromNetwork);
        Assert.Single(stub.Requests);

        clock.Now = T0.AddDays(2);
        mode = 1;
        var revalidated = await dl.GetAsync("https://example.invalid/skins.json", TimeSpan.FromDays(1));
        Assert.True(revalidated.FromNetwork);
        Assert.Equal("[1]", File.ReadAllText(revalidated.FilePath));

        clock.Now = T0.AddDays(4);
        mode = 2;
        var offline = await dl.GetAsync("https://example.invalid/skins.json", TimeSpan.FromDays(1));
        Assert.True(offline.Stale);
        Assert.Equal("[1]", File.ReadAllText(offline.FilePath));
    }

    [Fact]
    public async Task PriceCrawlIsResumableAndConvertsUsd()
    {
        var db = Fx.TempDb();
        var clock = new ManualClock(T0);
        var options = new DataOptions();
        string Page(int start, int total, params (string Name, int Cents)[] rows) =>
            "{\"success\":true,\"start\":" + start + ",\"pagesize\":10,\"total_count\":" + total + ",\"results\":["
            + string.Join(",", rows.Select(r => "{\"hash_name\":\"" + r.Name + "\",\"sell_price\":" + r.Cents
                + ",\"sell_price_text\":\"$" + (r.Cents / 100.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)
                + "\",\"sell_listings\":5}")) + "]}";
        bool failSecondPage = true;
        var stub = new StubHandler(req =>
        {
            var url = req.RequestUri!.ToString();
            if (url.Contains("priceoverview", StringComparison.Ordinal))
            {
                return StubHandler.Json(url.Contains("currency=7", StringComparison.Ordinal)
                    ? "{\"success\":true,\"lowest_price\":\"R$ 55,00\",\"median_price\":\"R$ 50,00\"}"
                    : "{\"success\":true,\"lowest_price\":\"$11.00\",\"median_price\":\"$10.00\"}");
            }

            if (url.EndsWith("&start=0", StringComparison.Ordinal))
            {
                return StubHandler.Json(Page(0, 12, ("A", 100), ("B", 250)));
            }

            if (failSecondPage)
            {
                return new HttpResponseMessage(HttpStatusCode.InternalServerError);
            }

            return StubHandler.Json(Page(10, 12, ("C", 1000), ("D", 0)));
        });
        var provider = new SteamMarketPriceProvider(new HttpClient(stub), db, options, clock);

        await Assert.ThrowsAsync<HttpRequestException>(() => provider.RefreshAllAsync());
        Assert.Equal(2, (await provider.GetCachedAsync()).Count);

        failSecondPage = false;
        int searchCallsBefore = stub.Requests.Count(r => r.RequestUri!.ToString().Contains("search", StringComparison.Ordinal));
        await provider.RefreshAllAsync();
        int searchCalls = stub.Requests.Count(r => r.RequestUri!.ToString().Contains("search", StringComparison.Ordinal)) - searchCallsBefore;
        Assert.Equal(1, searchCalls); // resumed at start=10

        var prices = await provider.GetCachedAsync();
        Assert.Equal(3, prices.Count); // "D" has no listing
        Assert.Equal(500, prices["A"].PriceCents); // $1.00 * (R$ 50,00 / $10.00)
        Assert.Equal(5000, prices["C"].PriceCents);
        Assert.False(prices["A"].IsStale);

        clock.Now = T0.AddHours(7);
        Assert.True((await provider.GetCachedAsync())["A"].IsStale);
    }

    [Fact]
    public async Task InventoryPaginatesCachesAndReportsPrivate()
    {
        var db = Fx.TempDb();
        var clock = new ManualClock(T0);
        var stub = new StubHandler(req => req.RequestUri!.Query.Contains("start_assetid=1003", StringComparison.Ordinal)
            ? StubHandler.Json(Fx.Text("samples/steam-inventory-page2.json"))
            : req.RequestUri.AbsolutePath.Contains("76561190000000000", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.Forbidden)
                : StubHandler.Json(Fx.Text("samples/steam-inventory-page1.json")));
        var client = new SteamInventoryClient(new HttpClient(stub), db, new DataOptions(), clock);

        var snap = await client.LoadAsync("76561197960287930");
        Assert.Equal(4, snap.Items.Count);
        Assert.False(snap.FromCache);
        Assert.Equal(T0.AddMinutes(10), snap.NextRefreshAllowedAt);

        var again = await client.LoadAsync("76561197960287930", forceRefresh: true);
        Assert.True(again.FromCache); // within the 10-minute limit
        Assert.Equal(2, stub.Requests.Count);

        clock.Now = T0.AddMinutes(11);
        Assert.False((await client.LoadAsync("76561197960287930", forceRefresh: true)).FromCache);

        await Assert.ThrowsAsync<InventoryPrivateException>(() => client.LoadAsync("76561190000000000"));
    }

    [Fact]
    public void CandidateFactoryJoinsCatalogPricesColorsAndInventory()
    {
        using var fs = File.OpenRead(Fx.Path("recorded/bymykel-skins-not-grouped.slice.json"));
        var catalog = CatalogParser.ParseSkinsNotGrouped(fs);
        using var ci = File.OpenRead(Fx.Path("recorded/color-index.slice.json"));
        var colors = Colors.ColorIndexParser.Parse(ci);
        var prices = new Dictionary<string, PriceQuote>
        {
            ["AK-47 | Redline (Field-Tested)"] = new("AK-47 | Redline (Field-Tested)", 14000, 7, 500, 120, "steam", T0, false),
        };
        var inv = InventoryParser.Parse(Fx.Text("samples/steam-inventory-page1.json"), "76561197960287930").Items;
        var cands = CandidateFactory.Build(catalog, prices, colors, inv);
        var market = cands.Single(c => !c.Owned);
        Assert.Equal(14000, market.PriceCents);
        Assert.NotNull(market.Colors);
        Assert.Equal(120, market.SalesVolume);
        var owned = cands.Single(c => c.Owned && c.ItemId == "owned:1001");
        Assert.Equal(0.2512, owned.FloatMin);
        Assert.Equal(14000, owned.PriceCents);
        Assert.IsType<SkinColorProfile>(owned.Colors);
    }
}
