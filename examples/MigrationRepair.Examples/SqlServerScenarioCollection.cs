using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MigrationRepair.Examples;

[CollectionDefinition("SqlServer scenario")]
public sealed class SqlServerScenarioCollection : ICollectionFixture<SqlServerScenarioFixture>
{
}

public sealed class SqlServerScenarioFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<ScenarioDbContext>()
            .UseSqlServer(TestSqlConfig.TestRunConnectionString)
            .Options;

        await using var db = new ScenarioDbContext(options);
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => default;
}
