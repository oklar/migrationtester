using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MigrationTester.Tests;

/// <summary>
/// Groups SQL integration tests so they do not run in parallel against the same database,
/// and drops the test database once at the start of each <c>dotnet test</c> run.
/// </summary>
[CollectionDefinition("SqlServer integration")]
public sealed class SqlServerIntegrationCollection : ICollectionFixture<SqlServerIntegrationFixture>
{
}

/// <summary>
/// Drops <see cref="TestSqlServerConfig.TestRunConnectionString"/> before any integration test runs.
/// After the session, the database remains for SSMS inspection; the next run drops it again here.
/// </summary>
public sealed class SqlServerIntegrationFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
            .Options;

        await using var db = new TestDbContext(options);
        await db.Database.EnsureDeletedAsync(TestContext.Current.CancellationToken);
    }

    public ValueTask DisposeAsync() => default;
}
