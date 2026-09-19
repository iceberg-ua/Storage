namespace Storage.Core.Repositories;

/// <summary>
/// Builds the "contains" patterns used by the item and location searches, so both
/// escape user input the same way and agree on the escape character.
/// </summary>
internal static class LikePattern
{
    /// <summary>Escape character passed to <c>EF.Functions.Like</c>.</summary>
    public const string Escape = "\\";

    /// <summary>
    /// Folds <paramref name="term"/> to lower case and wraps it as a "contains"
    /// pattern, neutralising the LIKE wildcards on the way — so searching for "50%"
    /// looks for that text rather than for anything starting with "50".
    /// </summary>
    public static string Contains(string term)
    {
        var escaped = term
            .ToLowerInvariant()
            .Replace(Escape, Escape + Escape)
            .Replace("%", Escape + "%")
            .Replace("_", Escape + "_");

        return $"%{escaped}%";
    }
}
