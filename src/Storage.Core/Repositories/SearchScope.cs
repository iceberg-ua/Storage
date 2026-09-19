namespace Storage.Core.Repositories;

/// <summary>
/// Which field(s) a search query is matched against.
/// </summary>
public enum SearchScope
{
    /// <summary>Name, description and tags at once, as a single result set.</summary>
    Everything,
    Name,
    Description,
    Tags
}
