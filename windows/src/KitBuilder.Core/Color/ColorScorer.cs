namespace KitBuilder.Core.Color;

/// <summary>
/// Color matching rules shared by every platform. See docs/kit-engine.md.
/// </summary>
public static class ColorScorer
{
    /// <summary>Gaussian width in OKLab units: strict (tolerance 0) to free (tolerance 1).</summary>
    public static double Sigma(double tolerance) => 0.08 + 0.17 * Math.Clamp(tolerance, 0, 1);

    /// <summary>Minimum color score a candidate needs to be considered.</summary>
    public static double Threshold(double tolerance) => 0.45 - 0.35 * Math.Clamp(tolerance, 0, 1);

    /// <summary>Penalty factor for multi-color finishes (actual colors vary per item).</summary>
    public const double MultiColorFactor = 0.9;

    /// <summary>
    /// Weight of the lightness difference. Skin images include shading, so their
    /// colors are darker than the "ideal" color a user picks; hue and chroma matter more.
    /// </summary>
    public const double LightnessWeight = 0.5;

    /// <summary>OKLab deltaE with lightness weighted by <see cref="LightnessWeight"/>.</summary>
    public static double Distance(OkLab x, OkLab y)
    {
        double dl = LightnessWeight * (x.L - y.L), da = x.A - y.A, db = x.B - y.B;
        return Math.Sqrt(dl * dl + da * da + db * db);
    }

    /// <summary>
    /// Score in [0,1]: max of (a) how much of the skin is close to the target and
    /// (b) the best single color, credited by its visible area (sqrt so accents count).
    /// </summary>
    public static double Score(OkLab target, SkinColorProfile? profile, double tolerance)
    {
        if (profile is null || profile.Colors.Count == 0)
        {
            return 0;
        }

        double sigma = Sigma(tolerance);
        double area = 0, accent = 0;
        foreach (var c in profile.Colors)
        {
            double d = Distance(target, c.Lab) / sigma;
            double sim = Math.Exp(-d * d);
            area += c.Weight * sim;
            accent = Math.Max(accent, sim * Math.Min(1.0, Math.Sqrt(c.Weight / 0.20)));
        }

        double score = Math.Max(area, accent);
        return profile.IsMultiColor ? score * MultiColorFactor : score;
    }

    /// <summary>How well two skins go together, in [0,1].</summary>
    public static double Harmony(SkinColorProfile? a, SkinColorProfile? b) => Harmony(a?.Signature, b?.Signature);

    public static double Harmony(PaletteColor? sa, PaletteColor? sb)
    {
        if (sa is null || sb is null)
        {
            return 0.5;
        }

        // Neutral pieces (black/gray/white) combine with anything.
        if (sa.Lab.Chroma < SkinColorProfile.ColoredChroma || sb.Lab.Chroma < SkinColorProfile.ColoredChroma)
        {
            return 0.7;
        }

        double d = Distance(sa.Lab, sb.Lab) / 0.15;
        return Math.Exp(-d * d);
    }
}
