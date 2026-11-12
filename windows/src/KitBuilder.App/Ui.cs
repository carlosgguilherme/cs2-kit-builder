using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.UI;

namespace KitBuilder.App;

/// <summary>Small conversions used from XAML with x:Bind function bindings.</summary>
public static class Ui
{
    public static SolidColorBrush Brush(string? hex) => new(ParseColor(hex));

    public static Color ParseColor(string? hex)
    {
        var h = (hex ?? "").TrimStart('#');
        if (h.Length == 6 && uint.TryParse(h, System.Globalization.NumberStyles.HexNumber, null, out var v))
        {
            return Color.FromArgb(255, (byte)(v >> 16), (byte)(v >> 8), (byte)v);
        }

        return Color.FromArgb(255, 176, 195, 217);
    }

    public static string Hex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";

    public static ImageSource? Image(string? url) => string.IsNullOrEmpty(url) ? null : new BitmapImage(new Uri(url));

    public static Visibility Show(bool value) => value ? Visibility.Visible : Visibility.Collapsed;

    public static Visibility ShowText(string? text) => string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
}
