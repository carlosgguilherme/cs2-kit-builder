namespace KitBuilder.Core.Color;

/// <summary>One dominant color of a skin and its share of the visible area.</summary>
public sealed record PaletteColor(OkLab Lab, double Weight, string Hex);

/// <summary>Dominant palette of a skin (from color-index.json).</summary>
public sealed record SkinColorProfile(IReadOnlyList<PaletteColor> Colors, bool IsMultiColor)
{
    /// <summary>Chroma above which a palette color counts as "colored" (not gray/black/white).</summary>
    public const double ColoredChroma = 0.05;

    /// <summary>
    /// The color that best represents the skin when comparing it with other skins:
    /// the largest colored entry, or the largest entry when the skin is neutral.
    /// </summary>
    public PaletteColor? Signature
    {
        get
        {
            PaletteColor? best = null;
            foreach (var c in Colors)
            {
                if (c.Lab.Chroma >= ColoredChroma && (best is null || c.Weight > best.Weight))
                {
                    best = c;
                }
            }

            if (best is not null)
            {
                return best;
            }

            foreach (var c in Colors)
            {
                if (best is null || c.Weight > best.Weight)
                {
                    best = c;
                }
            }

            return best;
        }
    }
}
