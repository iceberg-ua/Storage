using Storage.Core.Models;

namespace Storage.App.ViewModels;

/// <summary>
/// A <see cref="PlaceCard"/> shaped for the cover and caption that Home and the
/// Locations tab draw: the location's own photo when it has one, else a 2×2 mosaic of
/// item photos from inside it, else a plain placeholder.
/// </summary>
public sealed class PlaceTile
{
    public PlaceTile(PlaceCard card)
    {
        Card = card;

        var photos = card.ItemPhotos;
        Photo1 = photos.ElementAtOrDefault(0);
        Photo2 = photos.ElementAtOrDefault(1);
        Photo3 = photos.ElementAtOrDefault(2);
        Photo4 = photos.ElementAtOrDefault(3);
    }

    public PlaceCard Card { get; }

    public int LocationId => Card.Location.Id;

    public string Name => Card.Location.Name;

    public string? Description => Card.Location.Description;

    public string? OwnPhoto => Card.Location.PhotoFileName;

    public bool HasOwnPhoto => OwnPhoto is not null;

    // Four named slots rather than a list bound by index: a missing photo is a null the
    // XAML hides, not an index out of range inside a compiled binding.
    public string? Photo1 { get; }
    public string? Photo2 { get; }
    public string? Photo3 { get; }
    public string? Photo4 { get; }

    public bool HasMosaic => !HasOwnPhoto && Photo1 is not null;

    public bool HasNoPicture => !HasOwnPhoto && Photo1 is null;

    /// <summary>"64 items · 3 inside": everything below it, and how many locations directly inside.</summary>
    public string Meta
    {
        get
        {
            var items = Card.ItemCount == 1 ? "1 item" : $"{Card.ItemCount} items";
            return Card.ChildCount == 0 ? items : $"{items} · {Card.ChildCount} inside";
        }
    }
}
