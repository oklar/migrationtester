using Microsoft.EntityFrameworkCore;
using MigrationTester.Runner;
using Xunit;

namespace MigrationTester.Tests;

[Collection("SqlServer integration")]
public sealed class MigrationAdversaryRunAllTests
{
    private static MigrationAdversaryBatchOptions Batch =>
        new()
        {
            CreateContext = () => new TestDbContext(new DbContextOptionsBuilder<TestDbContext>()
                .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
                .Options)
        };

    [Fact]
    public async Task RunAllAsync_single_migration_all_succeed()
    {
        var steps = await MigrationAdversaryRunner.RunAllAsync(
            new MigrationAdversaryBatchOptions
            {
                CreateContext = Batch.CreateContext,
                MigrationIds = ["20260319180720_Initial"]
            },
            TestContext.Current.CancellationToken);

        var single = Assert.Single(steps);
        Assert.Equal("20260319180720_Initial", single.MigrationId);
        Assert.True(single.Result.MigrationSucceeded, single.Result.FailureSummary);
        Assert.True(MigrationAdversaryRunner.AllStepsSucceeded(steps));
        Assert.Null(MigrationAdversaryRunner.FirstFailedStep(steps));
    }

    [Fact]
    public async Task RunAllAsync_full_chain_middle_step_fails_on_unique_with_hints()
    {
        var steps = await MigrationAdversaryRunner.RunAllAsync(Batch, TestContext.Current.CancellationToken);

        Assert.Equal(3, steps.Count);
        Assert.True(steps[0].Result.MigrationSucceeded, steps[0].Result.FailureSummary);
        Assert.False(steps[1].Result.MigrationSucceeded);
        Assert.Equal("20260319180738_AddUniqueCode", steps[1].MigrationId);
        Assert.NotNull(steps[1].Result.FailureSummary);
        Assert.Contains("IX_Items_Code", steps[1].Result.FailureSummary!, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Deduplicate", steps[1].Result.FailureSummary!, StringComparison.Ordinal);
        Assert.True(steps[2].Result.MigrationSucceeded, steps[2].Result.FailureSummary);

        Assert.False(MigrationAdversaryRunner.AllStepsSucceeded(steps));
        var failed = MigrationAdversaryRunner.FirstFailedStep(steps);
        Assert.NotNull(failed);
        Assert.Equal("20260319180738_AddUniqueCode", failed!.MigrationId);
    }

    [Fact]
    public async Task RunRangeAsync_slices_inclusive_order()
    {
        var steps = await MigrationAdversaryRunner.RunRangeAsync(
            Batch,
            fromMigrationIdInclusive: "20260319180738_AddUniqueCode",
            toMigrationIdInclusive: "20260319190000_AddDescription",
            TestContext.Current.CancellationToken);

        Assert.Equal(2, steps.Count);
        Assert.Equal("20260319180738_AddUniqueCode", steps[0].MigrationId);
        Assert.False(steps[0].Result.MigrationSucceeded);
        Assert.Equal("20260319190000_AddDescription", steps[1].MigrationId);
        Assert.True(steps[1].Result.MigrationSucceeded, steps[1].Result.FailureSummary);
    }

    [Fact]
    public async Task RunRangeAsync_rejects_combined_with_MigrationIds()
    {
        var bad = new MigrationAdversaryBatchOptions
        {
            CreateContext = Batch.CreateContext,
            MigrationIds = ["20260319180720_Initial"]
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            MigrationAdversaryRunner.RunRangeAsync(
                bad,
                "20260319180720_Initial",
                "20260319180738_AddUniqueCode",
                TestContext.Current.CancellationToken));
    }
}
