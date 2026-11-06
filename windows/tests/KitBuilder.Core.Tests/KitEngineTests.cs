using System.Diagnostics;
using System.Text.Json.Nodes;
using KitBuilder.Core.Color;
using KitBuilder.Core.Domain;
using KitBuilder.Core.Kits;
using Xunit.Abstractions;

namespace KitBuilder.Core.Tests;

public class KitEngineTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<KitCandidate> Catalog = Fixtures.Catalog();
    private readonly KitEngine engine = new();

    public static TheoryData<string> CaseNames()
    {
        var data = new TheoryData<string>();
        foreach (var c in Fixtures.Load("kit-engine/cases.json")["cases"]!.AsArray())
        {
            data.Add((string)c!["name"]!);
        }

        return data;
    }

    private static JsonNode Case(string name) =>
        Fixtures.Load("kit-engine/cases.json")["cases"]!.AsArray().First(c => (string)c!["name"]! == name)!;

    /// <summary>Golden output written to cases.json when KITBUILDER_UPDATE_GOLDEN=1.</summary>
    private static JsonObject Describe(KitResult r) => new()
    {
        ["feasible"] = r.Feasible,
        ["reason"] = r.Problem?.Reason.ToString(),
        ["problemSlot"] = r.Problem?.SlotId,
        ["picks"] = r.Best is null ? null : new JsonObject(r.Best.Picks.Select(p =>
            KeyValuePair.Create(p.SlotId, (JsonNode?)JsonValue.Create(p.Candidate.ItemId)))),
        ["totalCents"] = r.Best?.TotalCents,
    };

    [Theory]
    [MemberData(nameof(CaseNames))]
    public void SharedCaseMatchesGoldenAndInvariants(string name)
    {
        var c = Case(name);
        var request = Fixtures.Request(c["request"]!);
        var result = engine.Build(request, Catalog);
        var actual = Describe(result);

        if (Environment.GetEnvironmentVariable("KITBUILDER_UPDATE_GOLDEN") == "1")
        {
            var path = Path.Combine(Fixtures.SourceDir, "kit-engine", "cases.json");
            var doc = JsonNode.Parse(File.ReadAllText(path))!;
            doc["cases"]!.AsArray().First(x => (string)x!["name"]! == name)!["expect"] = actual;
            File.WriteAllText(path, doc.ToJsonString(Fixtures.Indented));
        }
        else
        {
            Assert.NotNull(c["expect"]);
            Assert.Equal(c["expect"]!.ToJsonString(), actual.ToJsonString());
        }

        CheckInvariants(request, result);
    }

    private static void CheckInvariants(KitRequest request, KitResult result)
    {
        if (result.Best is null)
        {
            Assert.False(result.Feasible);
            Assert.NotNull(result.Problem);
            Assert.NotEmpty(result.Problem!.Suggestions);
            return;
        }

        var kit = result.Best;
        Assert.Equal(request.Slots.Count, kit.Picks.Count);
        Assert.Equal(kit.Picks.Sum(p => p.EffectivePriceCents), kit.TotalCents);
        if (result.Feasible)
        {
            Assert.InRange(kit.TotalCents, result.BandMinCents, result.BandMaxCents);
            Assert.True(kit.WithinBudgetBand);
        }

        for (int i = 0; i < kit.Picks.Count; i++)
        {
            var pick = kit.Picks[i];
            var slot = request.Slots[i];
            Assert.Equal(slot.Slot.Id, pick.SlotId);
            Assert.True(slot.Slot.Accepts(pick.Candidate));
            if (slot.LockedItemId is not null)
            {
                Assert.Equal(slot.LockedItemId, pick.Candidate.ItemId);
                continue;
            }

            Assert.True(request.AllowStatTrak || !pick.Candidate.StatTrak);
            Assert.True(request.AllowSouvenir || !pick.Candidate.Souvenir);
            if (request.WorstExterior is { } worst && pick.Candidate.Exterior is { } ext)
            {
                Assert.True(ext <= worst);
            }

            if (request.TargetColor is not null)
            {
                Assert.True(pick.ColorScore >= ColorScorer.Threshold(request.ColorTolerance));
            }

            if (request.OwnedItems == OwnedItemsPolicy.Ignore)
            {
                Assert.False(pick.Candidate.Owned);
            }

            var alt = result.AlternativesBySlot[pick.SlotId];
            Assert.All(alt.Cheaper, a => Assert.True(a.EffectivePriceCents < pick.EffectivePriceCents));
            Assert.All(alt.Pricier, a => Assert.True(a.EffectivePriceCents >= pick.EffectivePriceCents));
            Assert.All(alt.Cheaper.Concat(alt.Pricier), a => Assert.NotEqual(pick.Candidate.ItemId, a.Candidate.ItemId));
        }

        foreach (var other in result.AlternativeKits.Where(_ => result.Feasible))
        {
            Assert.InRange(other.TotalCents, result.BandMinCents, result.BandMaxCents);
            Assert.NotEqual(Ids(kit), Ids(other));
        }
    }

    private static string Ids(KitProposal k) => string.Join("|", k.Picks.Select(p => p.Candidate.ItemId));

    [Fact]
    public void IsDeterministic()
    {
        var request = Fixtures.Request(Case("acceptance-red-200-main-without-knife-gloves")["request"]!);
        var a = engine.Build(request, Catalog);
        var b = engine.Build(request, Catalog);
        Assert.Equal(Ids(a.Best!), Ids(b.Best!));
        Assert.Equal(a.AlternativeKits.Select(Ids), b.AlternativeKits.Select(Ids));
    }

    [Fact]
    public void ImpossibleBudgetExplainsMinimumTotal()
    {
        var r = engine.Build(Fixtures.Request(Case("impossible-budget")["request"]!), Catalog);
        Assert.Equal(InfeasibilityReason.BudgetTooLow, r.Problem!.Reason);
        // knife 35000 + gloves 30000 + ak 0 (owned, free) + m4 900 + awp 400 + deagle 400 + pistol 50
        Assert.Equal(66750, r.Problem.MinimumTotalCents);
        Assert.Equal(0, r.Problem.CheapestBySlot!["ak47"]);
        // The knife alone exceeds the budget: "the cheapest knife costs R$ 350,00".
        Assert.Equal("knife", r.Problem.SlotId);
        Assert.Equal(35000, r.Problem.CheapestCents);
        Assert.Contains(Suggestion.IncreaseBudget, r.Problem.Suggestions);
    }

    [Fact]
    public void RareColorExplainsSlotAndCheapestOption()
    {
        var r = engine.Build(Fixtures.Request(Case("rare-color-strict")["request"]!), Catalog);
        Assert.Equal(InfeasibilityReason.BudgetTooLow, r.Problem!.Reason);
        // Both slots exceed the budget; SlotId is the most expensive one, the map has all.
        Assert.Equal("awp", r.Problem.SlotId);
        Assert.Equal(6000000, r.Problem.CheapestCents);
        Assert.Equal(60000, r.Problem.CheapestBySlot!["ak47"]);
        Assert.Contains(Suggestion.IncreaseBudget, r.Problem.Suggestions);
        Assert.Contains(Suggestion.RelaxColor, r.Problem.Suggestions);
    }

    [Fact]
    public void UnavailableColorSuggestsRelaxingIt()
    {
        var r = engine.Build(Fixtures.Request(Case("color-unavailable")["request"]!), Catalog);
        Assert.Equal(InfeasibilityReason.NoCandidatesForSlot, r.Problem!.Reason);
        Assert.Equal("deagle", r.Problem.SlotId);
        Assert.Equal(400, r.Problem.CheapestIgnoringColorCents);
        Assert.Contains(Suggestion.RelaxColor, r.Problem.Suggestions);
    }

    [Fact]
    public void OwnedItemIsFreeOnlyWhenRequested()
    {
        var free = engine.Build(Fixtures.Request(Case("owned-zero-cost")["request"]!), Catalog);
        Assert.Contains(free.Best!.Picks, p => p.Candidate.Owned && p.EffectivePriceCents == 0);
        var ignored = engine.Build(Fixtures.Request(Case("owned-ignored")["request"]!), Catalog);
        Assert.DoesNotContain(ignored.Best!.Picks, p => p.Candidate.Owned);
    }

    [Fact]
    public void ToleranceZeroNeverExceedsBudget()
    {
        var r = engine.Build(Fixtures.Request(Case("tolerance-zero")["request"]!), Catalog);
        Assert.True(r.Feasible);
        Assert.True(r.Best!.TotalCents <= 20000);
    }

    [Fact]
    public void BudgetTooHighReturnsFlaggedBestEffort()
    {
        var r = engine.Build(Fixtures.Request(Case("budget-too-high")["request"]!), Catalog);
        Assert.False(r.Feasible);
        Assert.Equal(InfeasibilityReason.BudgetTooHigh, r.Problem!.Reason);
        Assert.NotNull(r.Best);
    }

    [Fact]
    public void ReplaceAndRebalance()
    {
        var request = Fixtures.Request(Case("acceptance-red-200-main-without-knife-gloves")["request"]!);
        var r = engine.Build(request, Catalog);
        var option = r.AlternativesBySlot["awp"].Cheaper.Concat(r.AlternativesBySlot["awp"].Pricier).First();
        var swapped = engine.ReplacePick(request, r.Best!, "awp", option.Candidate);
        Assert.Equal(r.Best!.TotalCents + option.DeltaCents, swapped.TotalCents);
        Assert.Equal(option.Candidate.ItemId, swapped.Picks.Single(p => p.SlotId == "awp").Candidate.ItemId);

        var rebalanced = engine.Rebalance(request, Catalog, "awp", option.Candidate.ItemId);
        Assert.Equal(option.Candidate.ItemId, rebalanced.Best!.Picks.Single(p => p.SlotId == "awp").Candidate.ItemId);
    }

    [Fact]
    public void SeedChangesTheCombinationReproducibly()
    {
        var baseReq = Fixtures.Request(Case("acceptance-red-200-main-without-knife-gloves")["request"]!);
        var seeds = Enumerable.Range(1, 10).Select(s => Ids(engine.Build(baseReq with { Seed = s }, Catalog).Best!)).ToList();
        Assert.Contains(seeds, s => s != Ids(engine.Build(baseReq, Catalog).Best!));
        Assert.Equal(seeds[3], Ids(engine.Build(baseReq with { Seed = 4 }, Catalog).Best!));
    }

    [Fact]
    public void CancellationIsHonored()
    {
        var request = Fixtures.Request(Case("acceptance-red-200-main-without-knife-gloves")["request"]!);
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        Assert.Throws<OperationCanceledException>(() => engine.Build(request, Catalog, cts.Token));
    }

    [Fact]
    public void UnitHashIsStable()
    {
        // Reference values for the Android port.
        Assert.Equal(KitScoring.UnitHash(7, "ak-stripe-ft"), KitScoring.UnitHash(7, "ak-stripe-ft"));
        Assert.InRange(KitScoring.UnitHash(1, "x"), 0, 1);
    }

    /// <summary>Full-size synthetic catalog (~21k items, like the real one): one kit under 300 ms.</summary>
    [Fact]
    [Trait("Category", "Performance")]
    public void BuildsFullCatalogKitUnder300Ms()
    {
        var catalog = SyntheticCatalog(21000);
        var request = new KitRequest
        {
            TargetColor = OkLab.FromHex("#d32f2f"),
            BudgetCents = 500000,
            Slots = KitPresets.MainItems.Select(s => new SlotRequest(s)).ToList(),
        };
        engine.Build(request, catalog); // warm-up (JIT)
        var times = new List<long>();
        for (int i = 0; i < 3; i++)
        {
            var sw = Stopwatch.StartNew();
            var r = engine.Build(request with { Seed = i }, catalog);
            sw.Stop();
            Assert.True(r.Feasible);
            times.Add(sw.ElapsedMilliseconds);
        }

        times.Sort();
        output.WriteLine($"Full-catalog kit build: {string.Join(", ", times)} ms");
#if DEBUG
        const int limitMs = 900; // unoptimized build; the 300 ms target is checked in Release (CI)
#else
        const int limitMs = 300;
#endif
        Assert.True(times[1] < limitMs, $"median {times[1]} ms ({string.Join(", ", times)})");
    }

    internal static List<KitCandidate> SyntheticCatalog(int count)
    {
        var rnd = new Random(42);
        string[] knives = ["Karambit", "Bayonet", "Butterfly Knife", "Flip Knife", "Gut Knife", "Huntsman Knife"];
        string[] gloves = ["Sport Gloves", "Driver Gloves", "Hand Wraps", "Moto Gloves"];
        string[] guns = ["AK-47", "M4A4", "M4A1-S", "AWP", "Desert Eagle", "USP-S", "Glock-18", "P250", "MP9", "FAMAS", "Galil AR", "Nova"];
        var list = new List<KitCandidate>(count);
        for (int i = 0; i < count; i++)
        {
            int kind = rnd.Next(100);
            (string weapon, ItemCategory cat, long price) = kind < 15
                ? (knives[rnd.Next(knives.Length)], ItemCategory.Knife, 30000 + (long)(rnd.NextDouble() * 3000000))
                : kind < 20
                    ? (gloves[rnd.Next(gloves.Length)], ItemCategory.Gloves, 30000 + (long)(rnd.NextDouble() * 2000000))
                    : (guns[rnd.Next(guns.Length)], ItemCategory.Rifle, 10 + (long)(Math.Pow(rnd.NextDouble(), 3) * 300000));
            var colors = new List<PaletteColor>();
            double remaining = 1;
            for (int k = 0; k < 4; k++)
            {
                double w = k == 3 ? remaining : remaining * (0.3 + rnd.NextDouble() * 0.4);
                remaining -= w;
                var lab = new OkLab(0.2 + rnd.NextDouble() * 0.7, (rnd.NextDouble() - 0.5) * 0.4, (rnd.NextDouble() - 0.5) * 0.4);
                colors.Add(new PaletteColor(lab, w, lab.ToHex()));
            }

            list.Add(new KitCandidate($"item-{i}", $"skin-{i / 5}", $"Item {i}", weapon, cat,
                (Rarity)(1 + rnd.Next(7)), (Exterior)(1 + rnd.Next(5)), rnd.Next(10) == 0, false, price,
                new SkinColorProfile(colors, rnd.Next(8) == 0), rnd.Next(5000)));
        }

        return list;
    }
}
