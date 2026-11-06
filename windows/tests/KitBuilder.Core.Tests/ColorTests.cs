using KitBuilder.Core.Color;

namespace KitBuilder.Core.Tests;

public class ColorTests
{
    [Fact]
    public void OkLabMatchesSharedFixture()
    {
        var fx = Fixtures.Load("oklab.json");
        double tol = (double)fx["tolerance"]!;
        foreach (var c in fx["cases"]!.AsArray())
        {
            var lab = OkLab.FromHex((string)c!["hex"]!);
            Assert.True(Math.Abs(lab.L - (double)c["l"]!) < tol, (string)c["hex"]!);
            Assert.True(Math.Abs(lab.A - (double)c["a"]!) < tol, (string)c["hex"]!);
            Assert.True(Math.Abs(lab.B - (double)c["b"]!) < tol, (string)c["hex"]!);
        }
    }

    [Theory]
    [InlineData("#ff0000")]
    [InlineData("#1e5bd6")]
    [InlineData("#c9a227")]
    [InlineData("#000000")]
    public void HexRoundTrips(string hex) => Assert.Equal(hex, OkLab.FromHex(hex).ToHex());

    private static SkinColorProfile Profile(bool multi, params (string Hex, double W)[] colors) =>
        new(colors.Select(c => new PaletteColor(OkLab.FromHex(c.Hex), c.W, c.Hex)).ToList(), multi);

    [Fact]
    public void RedSkinScoresHighForRedAndLowForBlue()
    {
        var red = Profile(false, ("#c62828", 0.5), ("#1c1c1c", 0.5));
        Assert.True(ColorScorer.Score(OkLab.FromHex("#d32f2f"), red, 0.5) > 0.8);
        Assert.True(ColorScorer.Score(OkLab.FromHex("#1e5bd6"), red, 0.5) < 0.15);
    }

    [Fact]
    public void SmallAccentGetsPartialCreditAndMultiColorIsPenalized()
    {
        var accent = Profile(false, ("#1c1c1c", 0.92), ("#c62828", 0.08));
        double s = ColorScorer.Score(OkLab.FromHex("#c62828"), accent, 0.5);
        Assert.InRange(s, 0.5, 0.7); // sqrt(0.08/0.20) ~ 0.63
        var multi = Profile(true, ("#c62828", 1.0));
        Assert.Equal(0.9, ColorScorer.Score(OkLab.FromHex("#c62828"), multi, 0.5), 6);
    }

    [Fact]
    public void ToleranceWidensMatching()
    {
        var orange = Profile(false, ("#e65100", 1.0));
        var red = OkLab.FromHex("#d32f2f");
        Assert.True(ColorScorer.Score(red, orange, 1.0) > ColorScorer.Score(red, orange, 0.0));
        Assert.True(ColorScorer.Threshold(0) > ColorScorer.Threshold(1));
    }

    [Fact]
    public void HarmonyPrefersSimilarColorsAndToleratesNeutrals()
    {
        var red = Profile(false, ("#c62828", 1.0));
        var red2 = Profile(false, ("#b71c1c", 1.0));
        var blue = Profile(false, ("#1e5bd6", 1.0));
        var black = Profile(false, ("#1c1c1c", 1.0));
        Assert.True(ColorScorer.Harmony(red, red2) > 0.8);
        Assert.True(ColorScorer.Harmony(red, blue) < 0.1);
        Assert.Equal(0.7, ColorScorer.Harmony(red, black));
    }
}
