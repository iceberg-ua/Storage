namespace Storage.Core.Models;

/// <summary>
/// A location with how much is directly inside it, for lists that show counts without
/// loading every item and child just to count them.
/// </summary>
/// <param name="ItemCount">Items stored in this location itself, not in its children.</param>
/// <param name="ChildCount">Locations nested directly under this one.</param>
public sealed record LocationSummary(Location Location, int ItemCount, int ChildCount);
