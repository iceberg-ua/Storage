namespace Storage.Core.Models;

/// <summary>
/// A top-level location as a card: what is in it, counted through every level below,
/// and a few item photos to show when the location has no photo of its own.
/// </summary>
/// <param name="ItemCount">Items in this location and in every location nested under it.</param>
/// <param name="ChildCount">Locations directly inside this one.</param>
/// <param name="ItemPhotos">
/// Primary photos of up to four items from anywhere under this location, newest first.
/// </param>
public sealed record PlaceCard(Location Location, int ItemCount, int ChildCount, IReadOnlyList<string> ItemPhotos);
