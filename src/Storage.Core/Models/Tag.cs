namespace Storage.Core.Models;

public class Tag
{
    public int Id { get; set; }
    public required string Name { get; set; }

    // Hex, from TagPalette. Never null, so a chip never has to handle a missing colour.
    public string Color { get; set; } = TagPalette.Default;

    // Navigation property
    public ICollection<Item> Items { get; set; } = [];
}
