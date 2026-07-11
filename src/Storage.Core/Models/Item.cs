namespace Storage.Core.Models;

public class Item
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    // Free-text, comma-separated tags
    public string? Tags { get; set; }

    // Path to the item's photo on device storage
    public string? PhotoPath { get; set; }

    public DateTime CreatedAt { get; set; }
    public int? LocationId { get; set; }

    // Navigation property
    public Location? Location { get; set; }
}
