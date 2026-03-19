using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MigrationRepair.Tests;

public sealed class TestDbContextFactory : IDesignTimeDbContextFactory<TestDbContext>
{
    public TestDbContext CreateDbContext(string[] args)
    {
        var cs = TestSqlServerConfig.DesignTimeConnectionString;
        var o = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(cs)
            .Options;
        return new TestDbContext(o);
    }
}
