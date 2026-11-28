using System.Globalization;
using Storage.Core.Services;

namespace Storage.App.Converters;

// A photo is stored as a bare filename, so the list and detail templates need the
// service to turn it into something an Image can load. Registered as app-level
// resources in App.xaml.cs because it has a dependency and XAML can't inject one:
// PhotoSourceConverter for a photo shown large, ThumbnailSourceConverter for lists
// and strips, which then decode a small copy instead of the full photo.
public class PhotoSourceConverter : IValueConverter
{
    private readonly IPhotoService _photoService;
    private readonly bool _thumbnail;

    public PhotoSourceConverter(IPhotoService photoService, bool thumbnail = false)
    {
        _photoService = photoService;
        _thumbnail = thumbnail;
    }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var fileName = value as string;
        var path = _thumbnail ? _photoService.GetThumbnailPath(fileName) : _photoService.GetFullPath(fileName);
        return path is null ? null : ImageSource.FromFile(path);
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
