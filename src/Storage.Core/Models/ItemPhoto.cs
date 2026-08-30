namespace Storage.Core.Models;

/// <summary>
/// One photo of an item. <see cref="FileName"/> is a bare filename, never a path —
/// the directory belongs to <c>IPhotoService</c>, so a photo stays findable even if
/// the app's data directory moves.
/// </summary>
public class ItemPhoto
{
    public int Id { get; set; }
    public int ItemId { get; set; }
    public required string FileName { get; set; }

    // Position in the item's strip. 0 is the primary photo — the one the list
    // thumbnail shows and the one a later inference pass would run against.
    public int SortOrder { get; set; }

    // Navigation property
    public Item? Item { get; set; }
}
