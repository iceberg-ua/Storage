using System.Globalization;
using Storage.Core.Models;

namespace Storage.App.Converters;

/// <summary>
/// Picks black or white text for a chip from the colour behind it. The fixed palette
/// is all dark enough for white, but a custom colour can be any brightness, so the
/// text has to follow the background rather than assume it.
/// </summary>
public class HexToTextColorConverter : IValueConverter
{
    // WCAG's crossover point: above this relative luminance, black text has the
    // better contrast ratio; below it, white does.
    private const double LuminanceThreshold = 0.179;

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var hex = value as string;
        if (string.IsNullOrWhiteSpace(hex))
            hex = TagPalette.Default;

        Color color;
        try
        {
            color = Color.FromArgb(hex);
        }
        catch (Exception)
        {
            color = Color.FromArgb(TagPalette.Default);
        }

        var luminance =
            0.2126 * Linearize(color.Red) +
            0.7152 * Linearize(color.Green) +
            0.0722 * Linearize(color.Blue);

        return luminance > LuminanceThreshold ? Colors.Black : Colors.White;
    }

    // sRGB gamma removed, so the channels can be weighted as actual light.
    private static double Linearize(double channel) =>
        channel <= 0.03928
            ? channel / 12.92
            : Math.Pow((channel + 0.055) / 1.055, 2.4);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
