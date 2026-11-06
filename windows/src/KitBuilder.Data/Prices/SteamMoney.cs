using System.Globalization;
using System.Text.RegularExpressions;

namespace KitBuilder.Data.Prices;

/// <summary>Parses Steam price strings such as "R$ 1.234,56" or "$1,234.56".</summary>
public static partial class SteamMoney
{
    public static bool TryParse(string? text, out long cents, out int currency)
    {
        cents = 0;
        currency = 0;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var t = text.Trim();
        if (t.StartsWith("R$", StringComparison.Ordinal))
        {
            currency = SteamCurrency.Brl;
        }
        else if (t.StartsWith('$') || t.EndsWith("USD", StringComparison.Ordinal))
        {
            currency = SteamCurrency.Usd;
        }
        else
        {
            return false;
        }

        var m = Number().Match(t);
        if (!m.Success)
        {
            return false;
        }

        var digits = m.Value;
        // The last separator followed by exactly two digits is the decimal separator.
        int lastSep = Math.Max(digits.LastIndexOf(','), digits.LastIndexOf('.'));
        string whole = digits, frac = "00";
        if (lastSep >= 0 && digits.Length - lastSep - 1 == 2)
        {
            whole = digits[..lastSep];
            frac = digits[(lastSep + 1)..];
        }

        whole = whole.Replace(".", "", StringComparison.Ordinal).Replace(",", "", StringComparison.Ordinal);
        if (!long.TryParse(whole, NumberStyles.None, CultureInfo.InvariantCulture, out var w)
            || !long.TryParse(frac, NumberStyles.None, CultureInfo.InvariantCulture, out var f))
        {
            return false;
        }

        cents = w * 100 + f;
        return true;
    }

    [GeneratedRegex(@"\d[\d.,]*")]
    private static partial Regex Number();
}
