// Developer CLI: refresh Steam prices and build kits from the terminal.
//
//   dotnet run --project windows/tools/KitBuilder.Cli -- prices [--data <dir>]
//       Full Steam crawl (~1,600 requests, ~1.5 h with the built-in throttling; resumable).
//   dotnet run --project windows/tools/KitBuilder.Cli -- kit --budget 200 --color "#d32f2f" \
//       --slots ak47,m4,awp,deagle,pistol [--tolerance 10] [--color-tolerance 0.5] [--seed 0] \
//       [--colors <color-index.json path or URL; default: newest data release>] [--data <dir>] [--price-snapshot <file>]
//       --price-snapshot: JSON object { "market hash name": priceInBRL } used instead of the
//       Steam cache (offline experiments only).
using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using KitBuilder.Core.Color;
using KitBuilder.Core.Kits;
using KitBuilder.Data;
using KitBuilder.Data.Catalog;
using KitBuilder.Data.Prices;
using Microsoft.Extensions.DependencyInjection;

var opts = new Dictionary<string, string>(StringComparer.Ordinal);
for (int i = 1; i < args.Length - 1; i += 2)
{
    opts[args[i].TrimStart('-')] = args[i + 1];
}

var services = new ServiceCollection().AddKitBuilderData(o =>
{
    if (opts.TryGetValue("data", out var dir))
    {
        o.DataDirectory = dir;
    }
}).BuildServiceProvider();
var prices = services.GetRequiredService<IPriceProvider>();
var inv = CultureInfo.InvariantCulture;
string Brl(long cents) => (cents / 100m).ToString("C", CultureInfo.GetCultureInfo("pt-BR"));

switch (args.FirstOrDefault())
{
    case "prices":
    {
        var progress = new Progress<RefreshProgress>(p =>
            Console.WriteLine($"{DateTime.Now:HH:mm:ss} {p.Done}/{p.Total}" + (p.RetryAfter is { } r ? $" rate limited until {r:HH:mm:ss}" : "")));
        while (true)
        {
            try
            {
                await prices.RefreshAllAsync(progress);
                break;
            }
            catch (KitBuilder.Data.Http.RateLimitedException e)
            {
                var wait = e.RetryAfter - DateTimeOffset.UtcNow;
                Console.WriteLine($"rate limited, waiting {Math.Max(30, wait.TotalSeconds):0}s");
                await Task.Delay(wait > TimeSpan.FromSeconds(30) ? wait : TimeSpan.FromSeconds(30));
            }
        }

        Console.WriteLine($"done: {(await prices.GetCachedAsync()).Count} prices cached");
        break;
    }

    case "kit":
    {
        var catalogService = services.GetRequiredService<CatalogService>();
        catalogService.ColorIndexUrl = opts.GetValueOrDefault("colors");
        var (items, _, locale) = await catalogService.LoadItemsAsync();
        var colors = await catalogService.LoadColorIndexAsync();
        IReadOnlyDictionary<string, PriceQuote> quotes = opts.TryGetValue("price-snapshot", out var snap)
            ? JsonSerializer.Deserialize<Dictionary<string, decimal>>(File.ReadAllText(snap))!
                .ToDictionary(kv => kv.Key, kv => new PriceQuote(kv.Key, (long)(kv.Value * 100), SteamCurrency.Brl, null, null,
                    "snapshot", DateTimeOffset.UtcNow, false))
            : await prices.GetCachedAsync();
        var candidates = CandidateFactory.Build(items, quotes, colors);
        Console.WriteLine($"catalog={items.Count} ({locale}) colors={colors.Count} prices={quotes.Count} candidates={candidates.Count}");

        var byId = KitPresets.MainItems.ToDictionary(s => s.Id);
        var slots = opts.GetValueOrDefault("slots", "knife,gloves,ak47,m4,awp,deagle,pistol").Split(',')
            .Select(s => new SlotRequest(byId[s.Trim()])).ToList();
        var request = new KitRequest
        {
            TargetColor = opts.TryGetValue("color", out var hex) ? OkLab.FromHex(hex) : null,
            ColorTolerance = double.Parse(opts.GetValueOrDefault("color-tolerance", "0.5"), inv),
            BudgetCents = (long)(decimal.Parse(opts.GetValueOrDefault("budget", "200"), inv) * 100),
            BudgetTolerance = double.Parse(opts.GetValueOrDefault("tolerance", "10"), inv) / 100,
            Slots = slots,
            Seed = int.Parse(opts.GetValueOrDefault("seed", "0"), inv),
        };
        var engine = new KitEngine();
        engine.Build(request, candidates); // warm-up (JIT)
        var sw = Stopwatch.StartNew();
        var result = engine.Build(request, candidates);
        sw.Stop();
        Console.WriteLine($"built in {sw.ElapsedMilliseconds} ms; band {Brl(result.BandMinCents)} – {Brl(result.BandMaxCents)}; feasible={result.Feasible}");
        if (result.Problem is { } p)
        {
            Console.WriteLine($"problem: {p.Reason} slot={p.SlotId} cheapest={(p.CheapestCents is { } c ? Brl(c) : "-")} "
                + $"min-total={(p.MinimumTotalCents is { } m ? Brl(m) : "-")} suggestions={string.Join(",", p.Suggestions)}");
        }

        void Print(KitProposal k, string title)
        {
            Console.WriteLine($"\n{title}: total {Brl(k.TotalCents)} ({k.DeltaRatio:+0.0%;-0.0%} vs budget), harmony {k.ColorHarmony:0.00}, score {k.Score:0.000}");
            foreach (var pick in k.Picks)
            {
                Console.WriteLine($"  {pick.SlotId,-7} {Brl(pick.EffectivePriceCents),12}  {pick.Candidate.Name} [{pick.Candidate.Rarity}] color {pick.ColorScore:0.00}");
            }
        }

        if (result.Best is { } best)
        {
            Print(best, "BEST");
            foreach (var (alt, i) in result.AlternativeKits.Select((a, i) => (a, i)))
            {
                Print(alt, $"ALTERNATIVE {i + 1}");
            }

            Console.WriteLine("\nPer-slot alternatives (cheaper | pricier):");
            foreach (var (slot, alts) in result.AlternativesBySlot)
            {
                string Fmt(AlternativeOption o) => $"{o.Candidate.Name} ({(o.DeltaCents >= 0 ? "+" : "")}{Brl(o.DeltaCents)})";
                Console.WriteLine($"  {slot}: {string.Join("; ", alts.Cheaper.Take(2).Select(Fmt))} | {string.Join("; ", alts.Pricier.Take(2).Select(Fmt))}");
            }
        }

        break;
    }

    default:
        Console.WriteLine("usage: prices | kit --budget 200 --color \"#d32f2f\" --slots ak47,m4,awp,deagle,pistol --colors <color-index.json>");
        break;
}
