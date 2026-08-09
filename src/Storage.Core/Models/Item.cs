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

    // How many of this item are stored; always at least 1
    public int Quantity { get; set; } = 1;

    public DateTime CreatedAt { get; set; }
    public int? LocationId { get; set; }

    // Navigation property
    public Location? Location { get; set; }
}
