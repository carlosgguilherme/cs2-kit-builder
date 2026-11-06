using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace KitBuilder.Data.Inventory;

/// <summary>Accepts a profile URL, a SteamID64 or a vanity name and resolves it to a SteamID64 (no API key).</summary>
public sealed partial class SteamIdResolver(HttpClient http)
{
    public sealed record Parsed(string? SteamId64, string? Vanity);

    public static Parsed Parse(string input)
    {
        var s = input.Trim().TrimEnd('/');
        var m = ProfilesUrl().Match(s);
        if (m.Success)
        {
            return new Parsed(m.Groups[1].Value, null);
        }

        m = VanityUrl().Match(s);
        if (m.Success)
        {
            return new Parsed(null, m.Groups[1].Value);
        }

        if (Id64().IsMatch(s))
        {
            return new Parsed(s, null);
        }

        return VanityName().IsMatch(s) ? new Parsed(null, s) : throw new ProfileNotFoundException(input);
    }

    public async Task<string> ResolveAsync(string input, CancellationToken ct = default)
    {
        var p = Parse(input);
        if (p.SteamId64 is not null)
        {
            return p.SteamId64;
        }

        var xml = await http.GetStringAsync($"https://steamcommunity.com/id/{Uri.EscapeDataString(p.Vanity!)}/?xml=1", ct)
            .ConfigureAwait(false);
        return ParseProfileXml(xml) ?? throw new ProfileNotFoundException(input);
    }

    public static string? ParseProfileXml(string xml)
    {
        try
        {
            var id = XDocument.Parse(xml).Root?.Element("steamID64")?.Value;
            return id is not null && Id64().IsMatch(id) ? id : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    [GeneratedRegex(@"steamcommunity\.com/profiles/(7656119\d{10})", RegexOptions.IgnoreCase)]
    private static partial Regex ProfilesUrl();

    [GeneratedRegex(@"steamcommunity\.com/id/([A-Za-z0-9_-]{2,32})", RegexOptions.IgnoreCase)]
    private static partial Regex VanityUrl();

    [GeneratedRegex(@"^7656119\d{10}$")]
    private static partial Regex Id64();

    [GeneratedRegex(@"^[A-Za-z0-9_-]{2,32}$")]
    private static partial Regex VanityName();
}
