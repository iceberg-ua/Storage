namespace Storage.Core.Models;

/// <summary>
/// The colours a tag can take. Fixed rather than free-form so every chip is legible
/// without measuring anything: all ten are dark enough that white text on them clears
/// contrast in both light and dark themes.
/// </summary>
public static class TagPalette
{
    public static readonly string[] Colors =
    [
        "#C62828", // red
        "#AD1457", // pink
        "#6A1B9A", // purple
        "#283593", // indigo
        "#0277BD", // blue
        "#00695C", // teal
        "#2E7D32", // green
        "#EF6C00", // orange
        "#4E342E", // brown
        "#37474F"  // blue grey
    ];

    public const string Default = "#37474F";

    /// <summary>
    /// Spreads colours across tags without having to track which are taken. Passing
    /// the current tag count gives each new tag the next swatch around.
    /// </summary>
    public static string ForIndex(int index) => Colors[Math.Abs(index) % Colors.Length];
}
