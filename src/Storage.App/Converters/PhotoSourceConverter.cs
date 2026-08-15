using System.Globalization;
using Storage.Core.Services;

namespace Storage.App.Converters;

// Item.PhotoPath is a bare filename, so the list and detail templates need the
// service to turn it into something an Image can load. Registered as an app-level
// resource in App.xaml.cs because it has a dependency and XAML can't inject one.
public class PhotoSourceConverter : IValueConverter
{
    private readonly IPhotoService _photoService;

    public PhotoSourceConverter(IPhotoService photoService)
    {
        _photoService = photoService;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fullPath = _photoService.GetFullPath(value as string);
        return fullPath is null ? null : ImageSource.FromFile(fullPath);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
