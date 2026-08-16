using System.Collections;
using System.Globalization;

namespace Storage.App.Converters;

/// <summary>
/// True when the bound collection or count is non-empty. Used to hide list sections
/// that would otherwise take up their margin with nothing in them.
/// </summary>
public class CountToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            int count => count > 0,
            ICollection collection => collection.Count > 0,
            _ => false
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
