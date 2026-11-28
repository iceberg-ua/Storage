namespace Storage.Core.Models;

/// <summary>
/// What deleting a location would affect: what sits inside it, and where that goes if
/// it is kept rather than deleted with it.
/// </summary>
/// <param name="Locations">Locations nested under it, at every level.</param>
/// <param name="Items">Items in it or in any location nested under it.</param>
/// <param name="DirectItems">Items in the location itself, the ones that move when it goes.
/// Items further down stay in their own location, which moves with them.</param>
/// <param name="ParentName">Where kept contents move to; null for the top level, where
/// sub-locations become top-level ones and items have no location.</param>
public sealed record LocationContents(int Locations, int Items, int DirectItems, string? ParentName)
{
    public bool IsEmpty => Locations == 0 && Items == 0;
}
