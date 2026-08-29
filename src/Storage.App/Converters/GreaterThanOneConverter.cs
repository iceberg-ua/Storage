using System.Globalization;

namespace Storage.App.Converters;

/// <summary>
/// True when the bound number is 2 or more. Hides the item list's quantity badge for
/// the single-quantity case, where "x1" is the default and carries no information.
/// <see cref="CountToBoolConverter"/> is not a substitute — it treats 1 as non-empty.
/// </summary>
public class GreaterThanOneConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            int count => count > 1,
            _ => false
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
