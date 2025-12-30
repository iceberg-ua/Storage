namespace Storage.Core.Models;

public class Location
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int? ParentId { get; set; }

    // Navigation properties
    public Location? Parent { get; set; }
    public ICollection<Location> Children { get; set; } = [];
    public ICollection<Item> Items { get; set; } = [];
}
