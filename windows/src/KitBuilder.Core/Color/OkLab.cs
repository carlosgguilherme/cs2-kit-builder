using System.Globalization;

namespace KitBuilder.Core.Color;

/// <summary>
/// OKLab color (Bjorn Ottosson). Formulas must stay identical to
/// data-pipeline/kitbuilder_pipeline/oklab.py and the Android port
/// (checked by shared/test-fixtures/oklab.json).
/// </summary>
public readonly record struct OkLab(double L, double A, double B)
{
    public double Chroma => Math.Sqrt(A * A + B * B);

    /// <summary>Hue in degrees [0, 360).</summary>
    public double Hue => (Math.Atan2(B, A) * 180 / Math.PI + 360) % 360;

    /// <summary>Euclidean distance in OKLab (deltaE OK). ~0.02 is barely visible.</summary>
    public double DeltaE(OkLab other)
    {
        double dl = L - other.L, da = A - other.A, db = B - other.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    public static OkLab FromRgb(byte r, byte g, byte b)
    {
        double lr = ToLinear(r / 255.0), lg = ToLinear(g / 255.0), lb = ToLinear(b / 255.0);
        double l = Math.Cbrt(0.4122214708 * lr + 0.5363325363 * lg + 0.0514459929 * lb);
        double m = Math.Cbrt(0.2119034982 * lr + 0.6806995451 * lg + 0.1073969566 * lb);
        double s = Math.Cbrt(0.0883024619 * lr + 0.2817188376 * lg + 0.6299787005 * lb);
        return new OkLab(
            0.2104542553 * l + 0.7936177850 * m - 0.0040720468 * s,
            1.9779984951 * l - 2.4285922050 * m + 0.4505937099 * s,
            0.0259040371 * l + 0.7827717662 * m - 0.8086757660 * s);
    }

    public static OkLab FromHex(string hex)
    {
        var h = hex.TrimStart('#');
        if (h.Length != 6)
        {
            throw new FormatException($"Expected #RRGGBB, got '{hex}'.");
        }

        return FromRgb(
            byte.Parse(h.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture),
            byte.Parse(h.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
    }

    /// <summary>Converts back to sRGB hex (clamped to gamut).</summary>
    public string ToHex()
    {
        double l = Math.Pow(L + 0.3963377774 * A + 0.2158037573 * B, 3);
        double m = Math.Pow(L - 0.1055613458 * A - 0.0638541728 * B, 3);
        double s = Math.Pow(L - 0.0894841775 * A - 1.2914855480 * B, 3);
        double r = +4.0767416621 * l - 3.3077115913 * m + 0.2309699292 * s;
        double g = -1.2684380046 * l + 2.6097574011 * m - 0.3413193965 * s;
        double b = -0.0041960863 * l - 0.7034186147 * m + 1.7076147010 * s;
        return $"#{ToByte(r):x2}{ToByte(g):x2}{ToByte(b):x2}";
    }

    private static double ToLinear(double c) => c <= 0.04045 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static int ToByte(double linear)
    {
        double c = Math.Clamp(linear, 0, 1);
        double srgb = c <= 0.0031308 ? 12.92 * c : 1.055 * Math.Pow(c, 1 / 2.4) - 0.055;
        return (int)Math.Clamp(Math.Round(srgb * 255), 0, 255);
    }
}
