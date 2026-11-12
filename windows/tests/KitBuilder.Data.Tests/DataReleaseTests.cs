using System.Net;
using KitBuilder.Data.Catalog;
using KitBuilder.Data.Http;

namespace KitBuilder.Data.Tests;

public class DataReleaseTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    private const string Base = "https://github.com/carlosgguilherme/cs2-kit-builder/releases/download/";

    [Fact]
    public void PicksNewestPublishedDataReleaseWithTheColorIndex()
    {
        Assert.Equal(Base + "data-20261109/color-index.json",
            DataReleaseLocator.ParseColorIndexUrl(Fx.Text("recorded/github-releases.2026-11-12.json")));
        // Skips the app release, the draft and releases without the asset.
        Assert.Equal(Base + "data-20261116/color-index.json",
            DataReleaseLocator.ParseColorIndexUrl(Fx.Text("samples/github-releases-mixed.json")));
        Assert.Null(DataReleaseLocator.ParseColorIndexUrl("[]"));
    }

    [Fact]
    public async Task CachesTheAnswerAndWorksOffline()
    {
        var db = Fx.TempDb();
        var clock = new ManualClock(T0);
        bool online = true;
        var stub = new StubHandler(req => online
            ? StubHandler.Json(Fx.Text("recorded/github-releases.2026-11-12.json"))
            : throw new HttpRequestException("offline"));
        var locator = new DataReleaseLocator(new HttpClient(stub), db, new DataOptions(), clock);

        Assert.Equal(Base + "data-20261109/color-index.json", await locator.GetColorIndexUrlAsync());
        Assert.Equal(Base + "data-20261109/color-index.json", await locator.GetColorIndexUrlAsync());
        Assert.Single(stub.Requests); // once per check interval
        Assert.Equal("https://api.github.com/repos/carlosgguilherme/cs2-kit-builder/releases?per_page=30",
            stub.Requests[0].RequestUri!.ToString());

        clock.Now = T0.AddDays(2);
        online = false;
        Assert.Equal(Base + "data-20261109/color-index.json", await locator.GetColorIndexUrlAsync());
    }

    [Fact]
    public async Task CatalogServiceUsesTheLocatorWhenNoUrlIsConfigured()
    {
        var db = Fx.TempDb();
        var options = new DataOptions { DataDirectory = Path.Combine(Path.GetTempPath(), "kitbuilder-tests", Guid.NewGuid().ToString("N")) };
        var stub = new StubHandler(req => req.RequestUri!.Host == "api.github.com"
            ? StubHandler.Json(Fx.Text("recorded/github-releases.2026-11-12.json"))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Fx.Text("recorded/color-index.slice.json")) });
        var http = new HttpClient(stub);
        var service = new CatalogService(new CachedDownloader(http, db, Path.Combine(options.DataDirectory, "cache")), options,
            new DataReleaseLocator(http, db, options));
        var colors = await service.LoadColorIndexAsync();
        Assert.Equal(6, colors.Count);
        Assert.Contains(stub.Requests, r => r.RequestUri!.ToString() == Base + "data-20261109/color-index.json");
    }
}
