using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Storage.Core.Data;

/// <summary>
/// Teaches every SQLite connection EF opens a <c>unicode_lower</c> function, so queries
/// can fold case the way .NET does rather than the way SQLite does.
/// </summary>
/// <remarks>
/// SQLite's own <c>lower()</c> and its NOCASE collation only fold ASCII — "Молоток" and
/// "молоток" are different strings to both of them. <see cref="string.ToLowerInvariant"/>
/// knows the full Unicode case mappings, so pushing it into SQL as a scalar function is
/// what makes search case-insensitive in every language the app might hold.
///
/// The registration is per-connection and lasts as long as the connection is open, which
/// is why it hangs off <see cref="ConnectionOpened"/> rather than being done once at
/// startup. It costs a full scan of the searched column, but a "contains" search can
/// never use an index anyway, so nothing is lost by folding at query time instead of
/// storing a second normalised copy of every name.
///
/// This hook only fires for connections EF opens itself. Code that opens its own
/// connection and hands it to EF — a test fixture, typically — has to call
/// <see cref="Register"/> on it, or any query using the function fails with
/// "no such function: unicode_lower".
/// </remarks>
public sealed class UnicodeLowerInterceptor : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        Register(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override Task ConnectionOpenedAsync(
        DbConnection connection,
        ConnectionEndEventData eventData,
        CancellationToken cancellationToken = default)
    {
        Register(connection);
        return base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    /// <summary>
    /// Adds <c>unicode_lower</c> to an already-open SQLite connection. Safe to call more
    /// than once on the same connection; the later registration replaces the earlier.
    /// </summary>
    public static void Register(DbConnection connection)
    {
        if (connection is not SqliteConnection sqlite)
            return;

        // Nullable in and out: a NULL Description has to stay NULL so LIKE yields NULL
        // and the row simply doesn't match, rather than matching the empty string.
        sqlite.CreateFunction(
            StorageDbContext.UnicodeLowerFunction,
            (string? value) => value?.ToLowerInvariant(),
            isDeterministic: true);
    }
}
