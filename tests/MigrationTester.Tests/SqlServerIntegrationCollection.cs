using Xunit;

namespace MigrationTester.Tests;

/// <summary>
/// Groups SQL integration tests so they run one at a time, and clears databases an interrupted
/// earlier run may have left behind before any of them starts.
/// </summary>
[CollectionDefinition("SqlServer integration")]
public sealed class SqlServerIntegrationCollection : ICollectionFixture<SqlServerIntegrationFixture>
{
}

public sealed class SqlServerIntegrationFixture : IAsyncLifetime
{
    public async ValueTask InitializeAsync()
    {
        foreach (string name in await TestSqlServerConfig.TestDatabasesAsync(TestContext.Current.CancellationToken))
        {
            await TestSqlServerConfig.DropDatabaseAsync(name, TestContext.Current.CancellationToken);
        }
    }

    public ValueTask DisposeAsync() => default;
}
