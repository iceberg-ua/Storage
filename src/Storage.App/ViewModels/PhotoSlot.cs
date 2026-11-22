using CommunityToolkit.Mvvm.ComponentModel;

namespace Storage.App.ViewModels;

/// <summary>
/// One photo in the edit form's strip. Being the primary is a fact about position — the
/// primary is the first of the set — which a photo cannot see for itself, so
/// <see cref="AddItemViewModel"/> stamps it whenever the collection changes.
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

    // The form draws the heart filled on this one, the one the lists show, and hollow
    // on the rest.
    [ObservableProperty]
    private bool _isPrimary;
}
