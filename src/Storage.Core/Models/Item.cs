namespace Storage.Core.Models;

public class Item
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    // How many of this item are stored; always at least 1
    public int Quantity { get; set; } = 1;

    public DateTime CreatedAt { get; set; }
    public int? LocationId { get; set; }

    // Navigation properties
    public Location? Location { get; set; }
    public ICollection<Tag> Tags { get; set; } = [];
    public ICollection<ItemPhoto> Photos { get; set; } = [];

    /// <summary>
    /// Filename of the photo that stands for the item — the list thumbnail. Null when
    /// the item has no photos. Computed rather than stored so the primary can never
    /// drift out of step with the set it is meant to be a member of.
    /// </summary>
    public string? PrimaryPhotoFileName =>
        Photos.OrderBy(p => p.SortOrder).FirstOrDefault()?.FileName;
}
