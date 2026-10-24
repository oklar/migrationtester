using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace MigrationTester.Examples;

public sealed class ScenarioDbContextFactory : IDesignTimeDbContextFactory<ScenarioDbContext>
{
    public ScenarioDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ScenarioDbContext>()
            .UseSqlServer(TestSqlConfig.DesignTimeConnectionString)
            .Options;

        return new ScenarioDbContext(options);
    }
}
