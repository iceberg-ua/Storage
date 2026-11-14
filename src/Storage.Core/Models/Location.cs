namespace Storage.Core.Models;

public class Location
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    // At most one photo, by design: a location wants one picture you recognise it by,
    // not a strip. A bare filename, like ItemPhoto.FileName — IPhotoService owns the
    // directory.
    public string? PhotoFileName { get; set; }

    public int? ParentId { get; set; }

    // Navigation properties
    public Location? Parent { get; set; }
    public ICollection<Location> Children { get; set; } = [];
    public ICollection<Item> Items { get; set; } = [];
}
