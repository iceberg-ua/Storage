using System.Globalization;
using Storage.Core.Models;

namespace Storage.App.Converters;

/// <summary>
/// Turns a tag's stored hex string into a <see cref="Color"/>. Anything unparseable
/// falls back to the palette default — a bad value should leave a chip plain, never
/// invisible.
/// </summary>
public class HexToColorConverter : IValueConverter
{
    private static readonly Color Fallback = Color.FromArgb(TagPalette.Default);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string hex || string.IsNullOrWhiteSpace(hex))
            return Fallback;

        try
        {
            return Color.FromArgb(hex);
        }
        catch (Exception)
        {
            return Fallback;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
