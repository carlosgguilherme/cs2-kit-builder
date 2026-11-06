// Verifies the external endpoints with real calls and records the raw answers
// into a local folder (default ./probe-output, ignored by git). Run it from a
// normal internet connection (cloud machines are often blocked by Steam):
//
//   dotnet run --project windows/tools/KitBuilder.EndpointProbe -- --steam-id <profile URL | SteamID64 | vanity>
//
// Then share probe-output/summary.md (it contains no inventory contents).
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Json;
using KitBuilder.Data.Inventory;
using KitBuilder.Data.Prices;

string outDir = "probe-output";
string? steamInput = null;
for (int i = 0; i < args.Length; i++)
{
    if (args[i] == "--out" && i + 1 < args.Length) outDir = args[++i];
    else if (args[i] == "--steam-id" && i + 1 < args.Length) steamInput = args[++i];
}

Directory.CreateDirectory(outDir);
var summary = new StringBuilder($"# Endpoint probe — {DateTimeOffset.UtcNow:yyyy-MM-dd HH:mm} UTC\n\n");
using var http = new HttpClient(new SocketsHttpHandler { AutomaticDecompression = DecompressionMethods.All });
http.DefaultRequestHeaders.UserAgent.ParseAdd("KitBuilder-EndpointProbe/0.1");

async Task<(HttpStatusCode Code, string Body, HttpResponseMessage Resp)> Get(string name, string url)
{
    await Task.Delay(TimeSpan.FromSeconds(4)); // be gentle with Steam
    var sw = Stopwatch.StartNew();
    var resp = await http.GetAsync(url);
    var body = await resp.Content.ReadAsStringAsync();
    File.WriteAllText(Path.Combine(outDir, name), body);
    summary.Append($"- `{name}` → HTTP {(int)resp.StatusCode} in {sw.ElapsedMilliseconds} ms, {body.Length} chars\n");
    Console.WriteLine($"{name}: {(int)resp.StatusCode}");
    return (resp.StatusCode, body, resp);
}

void Note(string text)
{
    summary.Append(text).Append('\n');
    Console.WriteLine(text);
}

// 1. Catalog (ByMykel) and conditional request headers.
summary.Append("\n## Catalog\n");
var cat = await Get("catalog-head.txt", "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/en/collections.json");
Note($"  ETag: {cat.Resp.Headers.ETag}, Last-Modified: {cat.Resp.Content.Headers.LastModified}");
var ptbr = await Get("catalog-ptbr-check.txt", "https://raw.githubusercontent.com/ByMykel/CSGO-API/main/public/api/pt-BR/collections.json");
Note($"  pt-BR folder available: {ptbr.Code == HttpStatusCode.OK}");

// 2. Market search (bulk prices).
summary.Append("\n## Market search (bulk)\n");
var s1 = await Get("steam-search-render.json", SteamMarketPriceProvider.SearchUrl(0) + "&currency=7");
if (s1.Code == HttpStatusCode.OK)
{
    var page = SteamMarketParser.ParseSearch(s1.Body);
    using var doc = JsonDocument.Parse(s1.Body);
    Note($"  total_count={page.TotalCount}, pagesize={doc.RootElement.GetProperty("pagesize")}, rows={page.Rows.Count}");
    Note($"  first sell_price_text: {doc.RootElement.GetProperty("results")[0].GetProperty("sell_price_text")} (currency parsed: {page.Rows[0].Currency})");
}

var s2 = await Get("steam-search-render-count100.json", SteamMarketPriceProvider.SearchUrl(0).Replace("count=10", "count=100"));
if (s2.Code == HttpStatusCode.OK)
{
    using var doc = JsonDocument.Parse(s2.Body);
    Note($"  count=100 → pagesize={doc.RootElement.GetProperty("pagesize")}, rows={doc.RootElement.GetProperty("results").GetArrayLength()}");
}

// 3. Price overview (single item, BRL and USD) and the implied rate.
summary.Append("\n## Price overview\n");
const string item = "AK-47 | Redline (Field-Tested)";
var brl = await Get("steam-priceoverview-brl.json", SteamMarketPriceProvider.OverviewUrl(item, SteamCurrency.Brl));
var usd = await Get("steam-priceoverview-usd.json", SteamMarketPriceProvider.OverviewUrl(item, SteamCurrency.Usd));
if (brl.Code == HttpStatusCode.OK && usd.Code == HttpStatusCode.OK)
{
    Note($"  BRL: {brl.Body}");
    Note($"  USD: {usd.Body}");
    var rate = SteamMarketPriceProvider.FxRateFromSteam(
        SteamMarketParser.ParseOverview(brl.Body, SteamCurrency.Brl), SteamMarketParser.ParseOverview(usd.Body, SteamCurrency.Usd));
    Note($"  implied USD→BRL rate: {rate}");
}

// 4. Inventory (optional).
if (steamInput is not null)
{
    summary.Append("\n## Inventory\n");
    string id;
    var parsed = SteamIdResolver.Parse(steamInput);
    if (parsed.SteamId64 is null)
    {
        var xml = await Get("steam-profile.xml", $"https://steamcommunity.com/id/{parsed.Vanity}/?xml=1");
        id = SteamIdResolver.ParseProfileXml(xml.Body) ?? throw new ProfileNotFoundException(steamInput);
        Note("  vanity resolved via ?xml=1: OK");
    }
    else
    {
        id = parsed.SteamId64;
    }

    var inv = await Get("steam-inventory-page1.json", SteamInventoryClient.Url(id, null));
    if (inv.Code == HttpStatusCode.OK)
    {
        using var doc = JsonDocument.Parse(inv.Body);
        var root = doc.RootElement;
        Note($"  top-level fields: {string.Join(", ", root.EnumerateObject().Select(p => p.Name))}");
        Note($"  total_inventory_count={root.GetProperty("total_inventory_count")}, more_items={(root.TryGetProperty("more_items", out var m) ? m.ToString() : "absent")}");
        Note($"  asset_properties present: {root.TryGetProperty("asset_properties", out var ap)}"
             + (ap.ValueKind == JsonValueKind.Array && ap.GetArrayLength() > 0 ? $" — example: {ap[0].GetProperty("asset_properties")}" : ""));
        var page = InventoryParser.Parse(inv.Body, id);
        Note($"  parsed items: {page.Items.Count}, with float: {page.Items.Count(i => i.Float is not null)}, with stickers: {page.Items.Count(i => i.Stickers.Count > 0)}, with inspect link: {page.Items.Count(i => i.InspectLink is not null)}");
    }
    else
    {
        Note("  (403 usually means the inventory or profile is private)");
    }
}

File.WriteAllText(Path.Combine(outDir, "summary.md"), summary.ToString());
Console.WriteLine($"\nSaved to {Path.GetFullPath(outDir)} — share summary.md.");
