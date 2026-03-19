using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MigrationRepair.Tests;

public sealed class TestDbContextFactory : IDesignTimeDbContextFactory<TestDbContext>
{
    public TestDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite("Data Source=design.db")
            .Options;
        return new TestDbContext(o);
    }
}
