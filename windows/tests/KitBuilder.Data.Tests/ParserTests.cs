using KitBuilder.Core.Domain;
using KitBuilder.Data.Catalog;
using KitBuilder.Data.Colors;
using KitBuilder.Data.Inventory;
using KitBuilder.Data.Prices;

namespace KitBuilder.Data.Tests;

public class ParserTests
{
    [Fact]
    public void ParsesRealCatalogSlice()
    {
        using var fs = File.OpenRead(Fx.Path("recorded/bymykel-skins-not-grouped.slice.json"));
        var items = CatalogParser.ParseSkinsNotGrouped(fs);
        Assert.Equal(7, items.Count);
        var redline = items.Single(i => i.MarketHashName == "AK-47 | Redline (Field-Tested)");
        Assert.Equal("AK-47", redline.Weapon);
        Assert.Equal(ItemCategory.Rifle, redline.Category);
        Assert.Equal(Rarity.Classified, redline.Rarity);
        Assert.Equal(Exterior.FieldTested, redline.Exterior);
        Assert.False(redline.StatTrak);
        Assert.True(items.Single(i => i.MarketHashName.StartsWith("StatTrak", StringComparison.Ordinal)).StatTrak);
        Assert.True(items.Single(i => i.MarketHashName.StartsWith("Souvenir", StringComparison.Ordinal)).Souvenir);
        var dopplers = items.Where(i => i.MarketHashName == "★ Karambit | Doppler (Factory New)").ToList();
        Assert.Equal(2, dopplers.Count);
        Assert.All(dopplers, d => Assert.NotNull(d.Phase));
        Assert.NotEqual(dopplers[0].SkinId, dopplers[1].SkinId);
        var vanilla = items.Single(i => i.MarketHashName == "★ Bayonet");
        Assert.Null(vanilla.Exterior);
        Assert.Equal(ItemCategory.Knife, vanilla.Category);

        var (min, max) = CatalogParser.VariantFloatRange(redline);
        Assert.Equal(0.15, min!.Value, 6);
        Assert.Equal(0.38, max!.Value, 6);
    }

    [Fact]
    public void ParsesColorIndexAndLinksBySkinId()
    {
        using var cat = File.OpenRead(Fx.Path("recorded/bymykel-skins-not-grouped.slice.json"));
        var items = CatalogParser.ParseSkinsNotGrouped(cat);
        using var fs = File.OpenRead(Fx.Path("recorded/color-index.slice.json"));
        var index = ColorIndexParser.Parse(fs);
        Assert.All(items, i => Assert.True(index.ContainsKey(i.SkinId), i.MarketHashName));
        var redline = index[items.Single(i => i.MarketHashName == "AK-47 | Redline (Field-Tested)").SkinId];
        Assert.Contains(redline.Colors, c => c.Lab.Chroma > 0.05); // the red accent is kept
        Assert.InRange(redline.Colors.Sum(c => c.Weight), 0.999, 1.001);
    }

    [Theory]
    [InlineData("R$ 1.234,56", 123456, SteamCurrency.Brl)]
    [InlineData("R$ 12,34", 1234, SteamCurrency.Brl)]
    [InlineData("R$ 5", 500, SteamCurrency.Brl)]
    [InlineData("$13.34", 1334, SteamCurrency.Usd)]
    [InlineData("$1,234.56", 123456, SteamCurrency.Usd)]
    [InlineData("$0.03", 3, SteamCurrency.Usd)]
    public void ParsesSteamMoney(string text, long cents, int currency)
    {
        Assert.True(SteamMoney.TryParse(text, out var c, out var cur));
        Assert.Equal(cents, c);
        Assert.Equal(currency, cur);
    }

    [Theory]
    [InlineData("")]
    [InlineData("12,34€")]
    [InlineData("R$ --")]
    public void RejectsUnknownMoney(string text) => Assert.False(SteamMoney.TryParse(text, out _, out _));

    [Fact]
    public void ParsesRecordedSearchPage()
    {
        var page = SteamMarketParser.ParseSearch(Fx.Text("recorded/steam-search-render.2026-11-06.json"));
        Assert.Equal(15770, page.TotalCount);
        Assert.Equal(0, page.Start);
        Assert.Equal(10, page.Rows.Count);
        var row = page.Rows[0];
        Assert.Equal("AK-47 | Aphrodite (Battle-Scarred)", row.HashName);
        Assert.Equal(1151, row.SellPriceCents);
        Assert.Equal(SteamCurrency.Usd, row.Currency); // "currency=7" was ignored by Steam
        Assert.Equal(634, row.Listings);
    }

    [Fact]
    public void ParsesPriceOverviewAndDerivesRate()
    {
        var brl = SteamMarketParser.ParseOverview(Fx.Text("recorded/steam-priceoverview-brl.2026-11-06.json"), SteamCurrency.Brl)!;
        Assert.Equal(17768, brl.LowestCents);
        Assert.Equal(86495, brl.MedianCents);
        Assert.Equal(56, brl.Volume);
        var usd = SteamMarketParser.ParseOverview(Fx.Text("recorded/steam-priceoverview-usd.2026-11-06.json"), SteamCurrency.Usd)!;
        Assert.Equal(3263, usd.LowestCents);
        Assert.Equal(86495 / 15890.0, SteamMarketPriceProvider.FxRateFromSteam(brl, usd)!.Value, 6);
        Assert.Null(SteamMarketParser.ParseOverview("{\"success\":false}", 7));
    }

    [Fact]
    public void ParsesInventoryPage()
    {
        var page = InventoryParser.Parse(Fx.Text("samples/steam-inventory-page1.json"), "76561197960287930");
        Assert.True(page.MoreItems);
        Assert.Equal("1003", page.LastAssetId);
        Assert.Equal(3, page.Items.Count);

        var ak = page.Items.Single(i => i.AssetId == "1001");
        Assert.Equal("AK-47 | Redline (Field-Tested)", ak.MarketHashName);
        Assert.Equal(ItemCategory.Rifle, ak.Category);
        Assert.Equal(Exterior.FieldTested, ak.Exterior);
        Assert.Equal("Classified", ak.RarityName);
        Assert.Equal("#d32ce6", ak.RarityColor);
        Assert.Equal("My Rifle", ak.NameTag);
        Assert.Equal(["Crown (Foil)", "Howling Dawn"], ak.Stickers.Select(s => s.Name));
        Assert.Equal("https://example.invalid/sticker-b.png", ak.Stickers[1].ImageUrl);
        Assert.Equal("Lil' Squirt", ak.Charms.Single().Name);
        Assert.Equal(0.2512, ak.Float);
        Assert.Equal(661, ak.PaintSeed);
        Assert.Equal("steam://rungame/730/76561202255233023/+csgo_econ_action_preview%20S76561197960287930A1001D4375617890163937543",
            ak.InspectLink);
        Assert.StartsWith(InventoryParser.ImageBase, ak.IconUrl);

        var knife = page.Items.Single(i => i.AssetId == "1002");
        Assert.True(knife.StatTrak);
        Assert.Equal(ItemCategory.Knife, knife.Category);
        Assert.Null(knife.Float);
        Assert.False(knife.Tradable);

        Assert.Equal(ItemCategory.Other, page.Items.Single(i => i.AssetId == "1003").Category);
    }

    [Theory]
    [InlineData("https://steamcommunity.com/profiles/76561197960287930/", "76561197960287930", null)]
    [InlineData("steamcommunity.com/id/gabelogannewell", null, "gabelogannewell")]
    [InlineData("76561197960287930", "76561197960287930", null)]
    [InlineData("some_vanity-name", null, "some_vanity-name")]
    public void ParsesProfileInput(string input, string? id, string? vanity)
    {
        var p = SteamIdResolver.Parse(input);
        Assert.Equal(id, p.SteamId64);
        Assert.Equal(vanity, p.Vanity);
    }

    [Fact]
    public void ParsesProfileXml()
    {
        Assert.Equal("76561197960287930", SteamIdResolver.ParseProfileXml(Fx.Text("samples/steam-profile.xml")));
        Assert.Null(SteamIdResolver.ParseProfileXml("<response><error>The specified profile could not be found.</error></response>"));
        Assert.Throws<ProfileNotFoundException>(() => SteamIdResolver.Parse("not a valid input!"));
    }
}
