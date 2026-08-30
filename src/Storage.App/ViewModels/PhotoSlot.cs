using CommunityToolkit.Mvvm.ComponentModel;

namespace Storage.App.ViewModels;

/// <summary>
/// One photo in the edit form's strip. The flags describe the photo's position in the
/// set, which a photo cannot see for itself — <see cref="AddItemViewModel"/> stamps
/// them whenever the collection changes.
/// </summary>
public partial class PhotoSlot : ObservableObject
{
    public PhotoSlot(string fileName, ImageSource? source)
    {
        FileName = fileName;
        Source = source;
    }

    public string FileName { get; }

    // Resolved once on the way in. Rebuilding it per notification would make the strip
    // reload every thumbnail each time one photo moves.
    public ImageSource? Source { get; }

    [ObservableProperty]
    private bool _isPrimary;

    [ObservableProperty]
    private bool _canMoveLeft;

    [ObservableProperty]
    private bool _canMoveRight;
}
