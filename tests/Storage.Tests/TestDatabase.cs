using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Storage.Core.Data;

namespace Storage.Tests;

/// <summary>
/// A real SQLite database in memory that, like the app's file, takes any number of
/// connections at once — so repositories can be handed a context factory exactly as in
/// the app, and parallel calls run on connections of their own. The provider, schema
/// (via EnsureCreated) and FK relationships are all the real thing.
/// </summary>
public sealed class TestDatabase : IDbContextFactory<StorageDbContext>, IDisposable
{
    // A shared-cache memory database lives as long as one connection to it is open.
    private readonly SqliteConnection _keepAlive;
    private readonly DbContextOptions<StorageDbContext> _options;

    public TestDatabase()
    {
        // Unpooled, so closing the keep-alive really frees the database.
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = $"storage-test-{Guid.NewGuid():N}",
            Mode = SqliteOpenMode.Memory,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();

        _keepAlive = new SqliteConnection(connectionString);
        _keepAlive.Open();

        // EF opens every connection itself, so the interceptor registers unicode_lower
        // the same way it does in the app.
        _options = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite(connectionString)
            .AddInterceptors(new UnicodeLowerInterceptor())
            .Options;

        using var context = CreateDbContext();
        context.Database.EnsureCreated();
    }

    public StorageDbContext CreateDbContext() => new(_options);

    public void Dispose() => _keepAlive.Dispose();
}
