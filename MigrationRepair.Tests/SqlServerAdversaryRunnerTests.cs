using Microsoft.EntityFrameworkCore;
using MigrationRepair.Runner;
using Xunit;

namespace MigrationRepair.Tests;

/// <summary>Requires local SQL Server; see <see cref="TestSqlServerConfig"/>.</summary>
public sealed class SqlServerAdversaryRunnerTests
{
    [Fact]
    public async Task RunAsync_first_migration_succeeds_on_clean_database()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
            .Options;

        var result = await MigrationAdversaryRunner.RunAsync(new MigrationAdversaryOptions
        {
            CreateContext = () => new TestDbContext(options),
            TargetMigrationId = "20260319180720_Initial",
            EnableFuzzing = false
        });

        Assert.True(result.MigrationSucceeded, result.FailureSummary ?? result.Exception?.ToString());
    }

    [Fact]
    public async Task RunAsync_unique_index_migration_fails_when_adversary_seeded_duplicates()
    {
        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
            .Options;

        var result = await MigrationAdversaryRunner.RunAsync(new MigrationAdversaryOptions
        {
            CreateContext = () => new TestDbContext(options),
            TargetMigrationId = "20260319180738_AddUniqueCode",
            EnableFuzzing = false
        });

        Assert.False(result.MigrationSucceeded);
        Assert.NotNull(result.Exception);
        Assert.NotNull(result.FailureSummary);
        Assert.Contains("IX_Items_Code", result.FailureSummary, StringComparison.OrdinalIgnoreCase);
    }
}
