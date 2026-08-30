using Microsoft.EntityFrameworkCore;
using Storage.Core.Models;

namespace Storage.Core.Data;

public class StorageDbContext : DbContext
{
    public StorageDbContext(DbContextOptions<StorageDbContext> options)
        : base(options)
    {
    }

    public DbSet<Item> Items => Set<Item>();
    public DbSet<ItemPhoto> ItemPhotos => Set<ItemPhoto>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Tag> Tags => Set<Tag>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Item entity
        modelBuilder.Entity<Item>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Description).HasMaxLength(1000);
            entity.Property(e => e.Quantity).IsRequired().HasDefaultValue(1);
            entity.Property(e => e.CreatedAt).IsRequired();

            // Read off Photos on demand, so there is no column to keep in step.
            entity.Ignore(e => e.PrimaryPhotoFileName);

            entity.HasOne(e => e.Location)
                .WithMany(l => l.Items)
                .HasForeignKey(e => e.LocationId)
                .OnDelete(DeleteBehavior.SetNull);

            // Photos belong to the item and to nothing else, so they go with it.
            // Cascade also means clearing the collection deletes the rows, which is
            // what ItemRepository.SetItemPhotosAsync relies on.
            entity.HasMany(e => e.Photos)
                .WithOne(p => p.Item!)
                .HasForeignKey(p => p.ItemId)
                .OnDelete(DeleteBehavior.Cascade);

            // Many-to-many through an "ItemTag" join table. EF supplies the join
            // itself — nothing needs to hang off the relationship, so there is no
            // entity class for it, only the column names and cascade behaviour.
            entity.HasMany(e => e.Tags)
                .WithMany(t => t.Items)
                .UsingEntity(
                    "ItemTag",
                    r => r.HasOne(typeof(Tag)).WithMany().HasForeignKey("TagId").OnDelete(DeleteBehavior.Cascade),
                    l => l.HasOne(typeof(Item)).WithMany().HasForeignKey("ItemId").OnDelete(DeleteBehavior.Cascade),
                    j => j.HasKey("ItemId", "TagId"));
        });

        // Configure ItemPhoto entity
        modelBuilder.Entity<ItemPhoto>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.FileName).IsRequired().HasMaxLength(500);
            entity.Property(e => e.SortOrder).IsRequired();

            // Every read of this table is "one item's photos, in order".
            entity.HasIndex(e => new { e.ItemId, e.SortOrder });
        });

        // Configure Tag entity
        modelBuilder.Entity<Tag>(entity =>
        {
            entity.HasKey(e => e.Id);

            // NOCASE is what makes "Tools" and "tools" one tag: it governs the unique
            // index below and every equality comparison EF translates against Name.
            entity.Property(e => e.Name).IsRequired().HasMaxLength(50).UseCollation("NOCASE");
            entity.Property(e => e.Color).IsRequired().HasMaxLength(7);
            entity.HasIndex(e => e.Name).IsUnique();
        });

        // Configure Location entity with self-referencing hierarchy
        modelBuilder.Entity<Location>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(100);

            entity.HasOne(e => e.Parent)
                .WithMany(l => l.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
