using Microsoft.EntityFrameworkCore;
using MigrationTester.Tests.TestEntities;

namespace MigrationTester.Tests;

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
            e.Property(x => x.Description);
            e.HasIndex(x => x.Code).IsUnique();
        });
    }
}
