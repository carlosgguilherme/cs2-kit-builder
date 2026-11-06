using System.Text.Json;
using KitBuilder.Core.Color;

namespace KitBuilder.Data.Colors;

/// <summary>Reads color-index.json produced by /data-pipeline.</summary>
public static class ColorIndexParser
{
    public const int SupportedSchemaVersion = 1;

    public static IReadOnlyDictionary<string, SkinColorProfile> Parse(Stream json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int version = root.GetProperty("schemaVersion").GetInt32();
        if (version > SupportedSchemaVersion)
        {
            throw new NotSupportedException($"color-index schema {version} is newer than supported {SupportedSchemaVersion}.");
        }

        var map = new Dictionary<string, SkinColorProfile>(StringComparer.Ordinal);
        foreach (var s in root.GetProperty("skins").EnumerateArray())
        {
            var colors = s.GetProperty("colors").EnumerateArray().Select(c => new PaletteColor(
                new OkLab(c.GetProperty("l").GetDouble(), c.GetProperty("a").GetDouble(), c.GetProperty("b").GetDouble()),
                c.GetProperty("weight").GetDouble(),
                c.GetProperty("hex").GetString() ?? "")).ToList();
            map[s.GetProperty("skinId").GetString()!] = new SkinColorProfile(colors, s.GetProperty("isMultiColor").GetBoolean());
        }

        return map;
    }
}
