using System.Text.Json;
using System.Text.Json.Nodes;
using KitBuilder.Core.Color;
using KitBuilder.Core.Domain;
using KitBuilder.Core.Kits;

namespace KitBuilder.Core.Tests;

internal static class Fixtures
{
    public static string Dir => Path.Combine(AppContext.BaseDirectory, "fixtures");

    /// <summary>Source path of the shared fixtures (used to regenerate golden output).</summary>
    public static string SourceDir
    {
        get
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "shared", "test-fixtures")))
            {
                dir = dir.Parent;
            }

            return Path.Combine(dir?.FullName ?? throw new DirectoryNotFoundException("shared/test-fixtures"), "shared", "test-fixtures");
        }
    }

    public static JsonNode Load(string relative) => JsonNode.Parse(File.ReadAllText(Path.Combine(Dir, relative)))!;

    public static IReadOnlyList<KitCandidate> Catalog()
    {
        var list = new List<KitCandidate>();
        foreach (var c in Load("kit-engine/catalog.json")["candidates"]!.AsArray())
        {
            var colors = c!["colors"]!.AsArray()
                .Select(x => new PaletteColor(OkLab.FromHex((string)x!["hex"]!), (double)x["weight"]!, (string)x["hex"]!))
                .ToList();
            list.Add(new KitCandidate(
                (string)c["itemId"]!, (string)c["skinId"]!, (string)c["name"]!, (string)c["weapon"]!,
                DomainNames.ParseCategory((string)c["category"]!), DomainNames.ParseRarity((string)c["rarity"]!),
                DomainNames.ParseExterior((string?)c["exterior"]), (bool)c["statTrak"]!, (bool)c["souvenir"]!,
                (long)c["priceCents"]!, new SkinColorProfile(colors, (bool)c["isMultiColor"]!),
                (int?)c["salesVolume"], Owned: (bool)c["owned"]!));
        }

        return list;
    }

    public static readonly IReadOnlyDictionary<string, SlotDefinition> SlotsById =
        KitPresets.MainItems.ToDictionary(s => s.Id);

    public static KitRequest Request(JsonNode r)
    {
        var slots = r["slots"]!.AsArray().Select(s => new SlotRequest(
            SlotsById[(string)s!["slot"]!], (double?)s["priority"] ?? 1.0, (string?)s["locked"])).ToList();
        return new KitRequest
        {
            TargetColor = r["targetHex"] is { } hex ? OkLab.FromHex((string)hex!) : null,
            ColorTolerance = (double?)r["colorTolerance"] ?? 0.5,
            BudgetCents = (long)r["budgetCents"]!,
            BudgetTolerance = (double?)r["budgetTolerance"] ?? 0.10,
            Slots = slots,
            AllowStatTrak = (bool?)r["allowStatTrak"] ?? false,
            AllowSouvenir = (bool?)r["allowSouvenir"] ?? false,
            WorstExterior = DomainNames.ParseExterior((string?)r["worstExterior"]),
            OwnedItems = Enum.Parse<OwnedItemsPolicy>((string?)r["ownedItems"] ?? nameof(OwnedItemsPolicy.ZeroCost)),
            Seed = (int?)r["seed"] ?? 0,
        };
    }

    public static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };
}
