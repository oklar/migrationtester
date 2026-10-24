using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MigrationTester.Tests;

public sealed class TestDbContextFactory : IDesignTimeDbContextFactory<TestDbContext>
{
    public TestDbContext CreateDbContext(string[] args)
    {
        string cs = TestSqlServerConfig.DesignTimeConnectionString;
        var o = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(cs)
            .Options;
        return new TestDbContext(o);
    }
}
