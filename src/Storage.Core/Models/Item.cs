namespace Storage.Core.Models;

public class Item
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public DateTime CreatedAt { get; set; }
    public int? LocationId { get; set; }

    // Navigation property
    public Location? Location { get; set; }
}
