using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;

namespace Storage.Core.Repositories;

internal static class LocationTree
{
    /// <summary>
    /// <paramref name="rootId"/> and every location nested under it, at any depth. One
    /// query for the links, walked in memory; a repeat ends the walk, in case a cycle
    /// saved before saves rejected them is still in the database.
    /// </summary>
    public static async Task<HashSet<int>> SubtreeIdsAsync(StorageDbContext context, int rootId)
    {
        var links = await context.Locations
            .AsNoTracking()
            .Where(l => l.ParentId != null)
            .Select(l => new { l.Id, ParentId = l.ParentId!.Value })
            .ToListAsync();

        var childrenOf = links.ToLookup(l => l.ParentId, l => l.Id);

        var subtree = new HashSet<int>();
        var pending = new Stack<int>([rootId]);
        while (pending.TryPop(out var id))
        {
            if (!subtree.Add(id))
                continue;

            foreach (var child in childrenOf[id])
                pending.Push(child);
        }

        return subtree;
    }
}
