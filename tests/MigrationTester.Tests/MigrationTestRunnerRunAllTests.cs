using Xunit;
using static MigrationTester.Tests.MigrationTestRunnerTests;

namespace MigrationTester.Tests;

[Collection("SqlServer integration")]
public sealed class MigrationTestRunnerRunAllTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task RunAllAsync_reports_each_step_and_the_failing_one()
    {
        var report = await MigrationTestRunner.RunAllAsync(Options(), Ct);

        Assert.Equal(
            [MigrationStepOutcome.Passed, MigrationStepOutcome.Failed, MigrationStepOutcome.Passed],
            report.Steps.Select(static s => s.Outcome));
        Assert.False(report.Succeeded);
        Assert.Equal("20260319180738_AddUniqueCode", Assert.Single(report.FailedSteps).MigrationId);

        string text = report.ToString();
        Assert.StartsWith("MigrationTester: 1 of 3 migrations failed.", text, StringComparison.Ordinal);
        Assert.Contains("IX_Items_Code", text, StringComparison.Ordinal);
        Assert.Contains("passed 20260319190000_AddDescription", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunAllAsync_runs_selected_migrations_in_apply_order()
    {
        var report = await MigrationTestRunner.RunAllAsync(
            Options(migrationIds: ["AddDescription", "20260319180720_Initial"]), Ct);

        Assert.Equal(
            ["20260319180720_Initial", "20260319190000_AddDescription"],
            report.Steps.Select(static s => s.MigrationId));
        Assert.True(report.Succeeded, report.ToString());
        report.ThrowIfFailed();
    }

    [Fact]
    public async Task RunRangeAsync_slices_inclusive_apply_order()
    {
        var report = await MigrationTestRunner.RunRangeAsync(Options(), "AddUniqueCode", "AddDescription", Ct);

        Assert.Equal(
            ["20260319180738_AddUniqueCode", "20260319190000_AddDescription"],
            report.Steps.Select(static s => s.MigrationId));
    }

    [Fact]
    public async Task RunRangeAsync_rejects_MigrationIds()
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            MigrationTestRunner.RunRangeAsync(Options(migrationIds: ["Initial"]), "Initial", "AddUniqueCode", Ct));
    }

    [Fact]
    public async Task Throwaway_database_is_named_after_the_configured_one_and_dropped()
    {
        var report = await MigrationTestRunner.RunAllAsync(Options(migrationIds: ["Initial"]), Ct);

        Assert.StartsWith(TestSqlServerConfig.TestDatabaseName + "_mt_", report.DatabaseName, StringComparison.Ordinal);
        Assert.False(report.DatabaseKept);
        Assert.Empty(report.Warnings);
        Assert.False(await TestSqlServerConfig.DatabaseExistsAsync(report.DatabaseName, Ct));
    }

    [Fact]
    public async Task KeepDatabase_leaves_the_last_migration_state()
    {
        var report = await MigrationTestRunner.RunAllAsync(Options(keepDatabase: true, migrationIds: ["Initial"]), Ct);
        try
        {
            Assert.True(report.DatabaseKept);
            Assert.True(await TestSqlServerConfig.DatabaseExistsAsync(report.DatabaseName, Ct));
            Assert.Contains($"database '{report.DatabaseName}' kept", report.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            await TestSqlServerConfig.DropDatabaseAsync(report.DatabaseName, Ct);
        }
    }

    [Fact]
    public async Task KeepDatabase_keeps_the_planted_data_of_the_last_step()
    {
        var report = await MigrationTestRunner.RunAllAsync(Options(keepDatabase: true, migrationIds: ["AddUniqueCode"]), Ct);
        try
        {
            Assert.Equal(MigrationStepOutcome.Failed, Assert.Single(report.Steps).Outcome);

            // The unique index was rolled back, so the planted duplicates are still there to look at.
            int duplicates = await TestSqlServerConfig.ScalarAsync<int>(
                report.DatabaseName, "SELECT COUNT(*) FROM [Items] GROUP BY [Code] HAVING COUNT(*) > 1;", Ct);
            Assert.True(duplicates >= 2);
        }
        finally
        {
            await TestSqlServerConfig.DropDatabaseAsync(report.DatabaseName, Ct);
        }
    }

    [Fact]
    public async Task DatabaseName_uses_that_database()
    {
        string name = TestSqlServerConfig.TestDatabaseName;

        var report = await MigrationTestRunner.RunAllAsync(Options(databaseName: name, migrationIds: ["Initial"]), Ct);

        Assert.Equal(name, report.DatabaseName);
        Assert.Equal(name, Assert.Single(report.Steps).DatabaseName);
        Assert.False(await TestSqlServerConfig.DatabaseExistsAsync(name, Ct));
    }
}
