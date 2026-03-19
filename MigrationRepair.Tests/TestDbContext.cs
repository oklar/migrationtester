using Microsoft.EntityFrameworkCore;
using MigrationRepair.Tests.TestEntities;

namespace MigrationRepair.Tests;

public sealed class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options)
        : base(options)
    {
    }

    public DbSet<Item> Items => Set<Item>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Item>(e =>
        {
            e.ToTable("Items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).IsRequired();
            e.HasIndex(x => x.Code).IsUnique();
        });
    }
}
