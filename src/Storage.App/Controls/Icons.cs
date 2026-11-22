using System.Globalization;

namespace Storage.App.Controls;

/// <summary>
/// The icons the mockups in docs/design/screens draw, as path data on a 24 × 24 grid,
/// copied from their SVGs. Circles and rounded rectangles are written out as arcs, since
/// path data has no shapes of its own. Drawn by <see cref="Icon"/>.
/// </summary>
public static class Icons
{
    public static readonly string Back = "M15 5l-7 7 7 7";
    public static readonly string Close = "M6 6l12 12M18 6L6 18";
    public static readonly string Plus = "M12 5v14M5 12h14";
    public static readonly string ChevronRight = "M9 6l6 6-6 6";
    public static readonly string ChevronDown = "M6 9l6 6 6-6";
    public static readonly string Check = "M5 12l5 5 9-10";
    public static readonly string Pencil = "M4 20h4L19 9l-4-4L4 16z";
    public static readonly string Trash = "M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13";
    public static readonly string Heart = "M12 20s-7-4.4-7-10A4 4 0 0 1 12 7.5 4 4 0 0 1 19 10c0 5.6-7 10-7 10z";
    public static readonly string Home = "M3 11l9-7 9 7v9a1 1 0 0 1-1 1h-5v-6H9v6H4a1 1 0 0 1-1-1z";

    public static readonly string Search = Circle(11, 11, 7) + "M20 20l-3.5-3.5";
    public static readonly string Camera = "M4 8h3l2-3h6l2 3h3v11H4z" + Circle(12, 13, 3.5);
    public static readonly string Pin = "M12 21s-7-6.2-7-11.5A7 7 0 0 1 19 9.5C19 14.8 12 21 12 21z" + Circle(12, 9.5, 2.5);
    public static readonly string Tag = "M3 12V4h8l10 10-8 8z" + Circle(7.5, 8.5, 1.5);
    public static readonly string Clock = Circle(12, 12, 9) + "M12 7v5l3 2";
    public static readonly string Photo = RoundRect(3, 5, 18, 14, 3) + Circle(9, 10, 1.6) + "M21 16l-5-5-8 8";

    /// <summary>The four squares that stand for locations.</summary>
    public static readonly string Places =
        RoundRect(3, 3, 7, 7, 2) + RoundRect(14, 3, 7, 7, 2) + RoundRect(3, 14, 7, 7, 2) + RoundRect(14, 14, 7, 7, 2);

    /// <summary>Three dots; drawn filled.</summary>
    public static readonly string More = Circle(5, 12, 1.8) + Circle(12, 12, 1.8) + Circle(19, 12, 1.8);

    private static string Circle(double cx, double cy, double r) =>
        Invariant($"M{cx - r} {cy}a{r} {r} 0 1 0 {2 * r} 0a{r} {r} 0 1 0 {-2 * r} 0z");

    private static string RoundRect(double x, double y, double w, double h, double r) =>
        Invariant($"M{x + r} {y}h{w - 2 * r}a{r} {r} 0 0 1 {r} {r}v{h - 2 * r}a{r} {r} 0 0 1 {-r} {r}h{-(w - 2 * r)}a{r} {r} 0 0 1 {-r} {-r}v{-(h - 2 * r)}a{r} {r} 0 0 1 {r} {-r}z");

    // Path data wants a full stop for decimals whatever the phone's language.
    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
