using Microsoft.EntityFrameworkCore;
using Xunit;

namespace MigrationTester.Tests;

/// <summary>Single-migration runs against SQL Server; see <see cref="TestSqlServerConfig"/>.</summary>
[Collection("SqlServer integration")]
public sealed class MigrationTestRunnerTests
{
    internal static MigrationTestOptions Options(
        string? databaseName = null,
        bool keepDatabase = false,
        IReadOnlyList<string>? migrationIds = null) =>
        new()
        {
            CreateContext = () => new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
                .Options),
            DatabaseName = databaseName,
            KeepDatabase = keepDatabase,
            MigrationIds = migrationIds
        };

    [Fact]
    public async Task RunAsync_first_migration_passes()
    {
        var step = await MigrationTestRunner.RunAsync(Options(), "20260319180720_Initial", TestContext.Current.CancellationToken);

        Assert.Equal(MigrationStepOutcome.Passed, step.Outcome);
        Assert.True(step.Succeeded, step.Summary);
    }

    [Fact]
    public async Task RunAsync_unique_index_migration_fails_on_planted_duplicates()
    {
        var step = await MigrationTestRunner.RunAsync(Options(), "AddUniqueCode", TestContext.Current.CancellationToken);

        Assert.Equal(MigrationStepOutcome.Failed, step.Outcome);
        Assert.Equal("20260319180738_AddUniqueCode", step.MigrationId);
        var failing = Assert.Single(step.FailingScenarios);
        Assert.Equal(ScenarioKind.DuplicateUniqueIndexOrConstraint, failing.Kind);
        Assert.Contains("SQL Server error 1505", step.Summary, StringComparison.Ordinal);
        Assert.Contains("IX_Items_Code", step.Summary, StringComparison.Ordinal);
        Assert.Contains("Deduplicate", step.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ThrowIfFailed_throws_with_the_summary()
    {
        var step = await MigrationTestRunner.RunAsync(Options(), "AddUniqueCode", TestContext.Current.CancellationToken);

        var ex = Assert.Throws<MigrationTestException>(step.ThrowIfFailed);
        Assert.Equal(step.Summary, ex.Message);
        Assert.Same(step, Assert.Single(ex.FailedSteps));
    }

    [Fact]
    public async Task RunAsync_unknown_migration_throws_before_touching_the_database()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            MigrationTestRunner.RunAsync(Options(), "NoSuchMigration", TestContext.Current.CancellationToken));
    }
}
