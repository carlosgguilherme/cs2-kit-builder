using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using KitBuilder.Core.Domain;

namespace KitBuilder.Data.Inventory;

/// <summary>
/// Parses /inventory/{steamid}/730/2 responses (assets + descriptions, joined on
/// classid/instanceid). Float/pattern come from "asset_properties" when present.
/// Format notes and what is verified live in docs/data-sources.md.
/// </summary>
public static partial class InventoryParser
{
    public const string ImageBase = "https://community.akamai.steamstatic.com/economy/image/";

    public static InventoryPage Parse(string json, string steamId64)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int total = root.TryGetProperty("total_inventory_count", out var tc) ? tc.GetInt32() : 0;
        bool more = root.TryGetProperty("more_items", out var mi) && (mi.ValueKind == JsonValueKind.True
            || (mi.ValueKind == JsonValueKind.Number && mi.GetInt32() == 1));
        string? last = root.TryGetProperty("last_assetid", out var la) ? la.GetString() : null;
        if (!root.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
        {
            return new InventoryPage([], false, null, total); // empty inventory
        }

        var descriptions = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var d in root.GetProperty("descriptions").EnumerateArray())
        {
            descriptions[Key(d)] = d;
        }

        var props = ParseAssetProperties(root);
        var items = new List<InventoryItem>();
        foreach (var a in assets.EnumerateArray())
        {
            var assetId = a.GetProperty("assetid").GetString()!;
            if (!descriptions.TryGetValue(Key(a), out var d))
            {
                continue;
            }

            props.TryGetValue(assetId, out var p);
            items.Add(ToItem(assetId, d, steamId64, p));
        }

        return new InventoryPage(items, more, last, total);
    }

    private static string Key(JsonElement e) =>
        e.GetProperty("classid").GetString() + "_" + (e.TryGetProperty("instanceid", out var i) ? i.GetString() : "0");

    /// <summary>Per-asset properties: float, pattern seed and every raw value by property id.</summary>
    private sealed record AssetProps(double? Float, int? Seed, IReadOnlyDictionary<int, string> Values);

    private static readonly AssetProps NoProps = new(null, null, new Dictionary<int, string>());

    private static Dictionary<string, AssetProps> ParseAssetProperties(JsonElement root)
    {
        var map = new Dictionary<string, AssetProps>(StringComparer.Ordinal);
        if (!root.TryGetProperty("asset_properties", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return map;
        }

        foreach (var entry in list.EnumerateArray())
        {
            if (!entry.TryGetProperty("assetid", out var idEl) || !entry.TryGetProperty("asset_properties", out var ps))
            {
                continue;
            }

            double? fl = null;
            int? seed = null;
            var values = new Dictionary<int, string>();
            foreach (var p in ps.EnumerateArray())
            {
                var name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                foreach (var field in (string[])["string_value", "int_value", "float_value"])
                {
                    if (PropertyId(p) >= 0 && p.TryGetProperty(field, out var raw))
                    {
                        values[PropertyId(p)] = raw.ValueKind == JsonValueKind.String ? raw.GetString() ?? "" : raw.GetRawText();
                    }
                }

                if (p.TryGetProperty("float_value", out var fv) && (name.Contains("Wear", StringComparison.OrdinalIgnoreCase)
                        || PropertyId(p) == 2)
                    && double.TryParse(fv.ValueKind == JsonValueKind.String ? fv.GetString() : fv.GetRawText(),
                        NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                {
                    fl = f;
                }
                else if (p.TryGetProperty("int_value", out var iv) && (name.Contains("Pattern", StringComparison.OrdinalIgnoreCase)
                             || PropertyId(p) == 1)
                         && int.TryParse(iv.ValueKind == JsonValueKind.String ? iv.GetString() : iv.GetRawText(),
                             NumberStyles.Integer, CultureInfo.InvariantCulture, out var s))
                {
                    seed = s;
                }
            }

            map[idEl.GetString()!] = new AssetProps(fl, seed, values);
        }

        return map;
    }

    private static int PropertyId(JsonElement p) =>
        p.TryGetProperty("propertyid", out var id) && id.ValueKind == JsonValueKind.Number ? id.GetInt32() : -1;

    private static InventoryItem ToItem(string assetId, JsonElement d, string steamId64, AssetProps? props)
    {
        props ??= NoProps;
        string? rarity = null, rarityColor = null, exterior = null, weapon = null, typeTag = null, quality = null;
        if (d.TryGetProperty("tags", out var tags))
        {
            foreach (var t in tags.EnumerateArray())
            {
                var cat = t.GetProperty("category").GetString();
                var internalName = t.TryGetProperty("internal_name", out var inn) ? inn.GetString() : null;
                var localized = t.TryGetProperty("localized_tag_name", out var ln) ? ln.GetString() : null;
                switch (cat)
                {
                    case "Rarity":
                        rarity = localized;
                        rarityColor = t.TryGetProperty("color", out var col) ? "#" + col.GetString() : null;
                        break;
                    case "Exterior":
                        exterior = localized;
                        break;
                    case "Weapon":
                        weapon = localized;
                        break;
                    case "Type":
                        typeTag = internalName;
                        break;
                    case "Quality":
                        quality = internalName;
                        break;
                }
            }
        }

        var stickers = new List<AppliedItem>();
        var charms = new List<AppliedItem>();
        if (d.TryGetProperty("descriptions", out var descs))
        {
            foreach (var x in descs.EnumerateArray())
            {
                var html = x.TryGetProperty("value", out var v) ? v.GetString() ?? "" : "";
                if (html.Contains("sticker_info", StringComparison.Ordinal))
                {
                    stickers.AddRange(Applied(html, "Sticker", "Patch"));
                }
                else if (html.Contains("keychain_info", StringComparison.Ordinal))
                {
                    charms.AddRange(Applied(html, "Charm", "Keychain"));
                }
            }
        }

        string? nameTag = null;
        if (d.TryGetProperty("fraudwarnings", out var fw))
        {
            foreach (var w in fw.EnumerateArray())
            {
                var m = NameTag().Match(w.GetString() ?? "");
                if (m.Success)
                {
                    nameTag = m.Groups[1].Value;
                }
            }
        }

        string? inspect = null;
        if (d.TryGetProperty("actions", out var actions))
        {
            foreach (var act in actions.EnumerateArray())
            {
                var link = act.TryGetProperty("link", out var l) ? l.GetString() : null;
                if (link is not null && link.Contains("csgo_econ_action_preview", StringComparison.Ordinal))
                {
                    inspect = FillInspectLink(link, steamId64, assetId, props.Values);
                    break;
                }
            }
        }

        string type = d.TryGetProperty("type", out var ty) ? ty.GetString() ?? "" : "";
        string mhn = d.TryGetProperty("market_hash_name", out var mh) ? mh.GetString() ?? "" : "";
        return new InventoryItem(
            assetId, mhn,
            d.TryGetProperty("name", out var nm) ? nm.GetString() ?? mhn : mhn,
            type, weapon, CategoryFromTypeTag(typeTag),
            rarity, rarityColor, DomainNames.ParseExterior(exterior),
            quality == "strange" || mhn.StartsWith("StatTrak", StringComparison.Ordinal) || mhn.Contains("★ StatTrak", StringComparison.Ordinal),
            quality == "tournament" || mhn.StartsWith("Souvenir", StringComparison.Ordinal),
            nameTag, stickers, charms, inspect, props.Float, props.Seed,
            d.TryGetProperty("icon_url", out var icon) ? ImageBase + icon.GetString() : null,
            d.TryGetProperty("tradable", out var tr) && tr.GetInt32() == 1,
            d.TryGetProperty("marketable", out var mk) && mk.GetInt32() == 1);
    }

    /// <summary>
    /// Fills an inspect link template: the old <c>%owner_steamid%</c>/<c>%assetid%</c> placeholders and
    /// the current <c>%propid:N%</c> ones (property N of the asset, e.g. 6 = "Item Certificate").
    /// Returns null when a property is missing, instead of a broken link.
    /// </summary>
    public static string? FillInspectLink(string template, string steamId64, string assetId, IReadOnlyDictionary<int, string> properties)
    {
        bool missing = false;
        var link = PropIdPlaceholder().Replace(template, m =>
        {
            if (properties.TryGetValue(int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture), out var v))
            {
                return v;
            }

            missing = true;
            return m.Value;
        });
        return missing ? null : link.Replace("%owner_steamid%", steamId64, StringComparison.Ordinal)
            .Replace("%assetid%", assetId, StringComparison.Ordinal);
    }

    public static ItemCategory CategoryFromTypeTag(string? tag) => tag switch
    {
        "CSGO_Type_Rifle" or "CSGO_Type_SniperRifle" => ItemCategory.Rifle,
        "CSGO_Type_Pistol" => ItemCategory.Pistol,
        "CSGO_Type_SMG" => ItemCategory.Smg,
        "CSGO_Type_Shotgun" or "CSGO_Type_Machinegun" => ItemCategory.Heavy,
        "CSGO_Type_Knife" => ItemCategory.Knife,
        "Type_Hands" => ItemCategory.Gloves,
        "Type_CustomPlayer" => ItemCategory.Agent,
        "CSGO_Type_Equipment" => ItemCategory.Equipment,
        _ => ItemCategory.Other,
    };

    /// <summary>Sticker/charm block: images in order, names after "Sticker: " separated by ", ".</summary>
    private static IEnumerable<AppliedItem> Applied(string html, params string[] labels)
    {
        var images = ImgSrc().Matches(html).Select(m => m.Groups[1].Value).ToList();
        var text = TagStrip().Replace(html, "\n");
        List<string> names = [];
        foreach (var label in labels)
        {
            var m = Regex.Match(text, label + @"s?:\s*([^\n]+)");
            if (m.Success)
            {
                names = m.Groups[1].Value.Split(", ").Select(s => s.Trim()).Where(s => s.Length > 0).ToList();
                break;
            }
        }

        for (int i = 0; i < Math.Max(names.Count, images.Count); i++)
        {
            yield return new AppliedItem(i < names.Count ? names[i] : "?", i < images.Count ? images[i] : null);
        }
    }

    [GeneratedRegex(@"%propid:(\d+)%")]
    private static partial Regex PropIdPlaceholder();

    [GeneratedRegex(@"Name Tag:\s*''(.*)''")]
    private static partial Regex NameTag();

    [GeneratedRegex(@"<img[^>]*src=""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex ImgSrc();

    [GeneratedRegex(@"<[^>]+>")]
    private static partial Regex TagStrip();
}
