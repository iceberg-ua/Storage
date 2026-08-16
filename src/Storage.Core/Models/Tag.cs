namespace Storage.Core.Models;

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // Navigation property
    public ICollection<Item> Items { get; set; } = [];
}
