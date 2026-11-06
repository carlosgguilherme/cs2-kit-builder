using System.Text.Json;
using KitBuilder.Core.Domain;

namespace KitBuilder.Data.Catalog;

public static class CatalogParser
{
    /// <summary>Parses ByMykel/CSGO-API skins_not_grouped.json.</summary>
    public static IReadOnlyList<CatalogItem> ParseSkinsNotGrouped(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var list = new List<CatalogItem>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var weapon = Str(e, "weapon", "name") ?? "";
            var category = DomainNames.ParseCategory(Str(e, "category", "name"));
            // ByMykel's "Heavy" mixes shotguns and machine guns; snipers are in "Rifles". Kept as-is.
            list.Add(new CatalogItem(
                Str(e, "id") ?? "",
                Str(e, "skin_id") ?? "",
                Str(e, "market_hash_name") ?? Str(e, "name") ?? "",
                Str(e, "name") ?? "",
                weapon,
                category,
                DomainNames.ParseRarity(Str(e, "rarity", "name")),
                Str(e, "rarity", "color") ?? "#b0c3d9",
                DomainNames.ParseExterior(Str(e, "wear", "name")),
                Bool(e, "stattrak"),
                Bool(e, "souvenir"),
                Str(e, "phase"),
                Num(e, "min_float"),
                Num(e, "max_float"),
                DomainNames.ParseTeam(Str(e, "team", "id")),
                Str(e, "image"),
                Str(e, "pattern", "name")));
        }

        return list;
    }

    /// <summary>Float range of a variant: the skin's range intersected with the exterior band.</summary>
    public static (double? Min, double? Max) VariantFloatRange(CatalogItem item)
    {
        if (item.Exterior is not { } ext)
        {
            return (item.MinFloat, item.MaxFloat);
        }

        var (bmin, bmax) = DomainNames.FloatBand(ext);
        return (Math.Max(bmin, item.MinFloat ?? 0), Math.Min(bmax, item.MaxFloat ?? 1));
    }

    private static string? Str(JsonElement e, params string[] path)
    {
        foreach (var p in path)
        {
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty(p, out e))
            {
                return null;
            }
        }

        return e.ValueKind == JsonValueKind.String ? e.GetString() : e.ValueKind == JsonValueKind.Number ? e.GetRawText() : null;
    }

    private static bool Bool(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    private static double? Num(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : null;
}
