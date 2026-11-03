namespace Storage.Core.Repositories;

/// <summary>
/// A location was given itself, or one of its own descendants, as its parent.
/// Its own type so callers can tell this refusal apart from EF Core's
/// <see cref="InvalidOperationException"/>s.
/// </summary>
public sealed class LocationCycleException(int locationId, int parentId)
    : InvalidOperationException(
        $"Location {locationId} can't be moved under itself or one of its own descendants ({parentId}).")
{
    public int LocationId { get; } = locationId;
    public int ParentId { get; } = parentId;
}
