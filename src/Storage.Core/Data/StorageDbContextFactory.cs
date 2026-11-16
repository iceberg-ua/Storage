using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Storage.Core.Data;

// Used only by the EF Core CLI (dotnet ef) at design time to build migrations.
// The app gets its contexts from the IDbContextFactory registered in MauiProgram.cs.
public class StorageDbContextFactory : IDesignTimeDbContextFactory<StorageDbContext>
{
    public StorageDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<StorageDbContext>()
            .UseSqlite("Data Source=storage.db")
            .Options;

        return new StorageDbContext(options);
    }
}
