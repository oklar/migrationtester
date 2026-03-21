using Microsoft.EntityFrameworkCore;
using MigrationRepair.Runner;

namespace MigrationRepair.Examples;

/// <summary>
/// These tests intentionally <b>fail</b> (red in the runner) when the adversary breaks the migration,
/// so xUnit prints <see cref="MigrationAdversaryResult.FailureSummary"/> and SQL hints as the assertion message.
/// Run: <c>dotnet test --logger "console;verbosity=detailed"</c>
/// </summary>
[Collection("SqlServer scenario")]
public sealed class AdversaryFailureDemoTests
{
    /// <summary>EF migration that adds <c>IX_Products_Code</c> (unique).</summary>
    public const string HostileMigrationId = "20260319195454_09_AddUniqueProductCode";

    private static DbContextOptions<ScenarioDbContext> TestOptions =>
        new DbContextOptionsBuilder<ScenarioDbContext>()
            .UseSqlServer(TestSqlConfig.TestRunConnectionString)
            .Options;

    private static MigrationAdversaryBatchOptions Batch =>
        new()
        {
            CreateContext = () => new ScenarioDbContext(TestOptions)
        };

    /// <summary>
    /// Fails (red) and surfaces <c>FailureSummary</c> because the adversary seeds duplicate <c>Code</c> values.
    /// </summary>
    [Fact]
    public async Task RunAsync_migration_red_test_prints_adversary_hints_in_message()
    {
        var result = await MigrationAdversaryRunner.RunAsync(
            new MigrationAdversaryOptions
            {
                CreateContext = Batch.CreateContext,
                TargetMigrationId = HostileMigrationId
            },
            TestContext.Current.CancellationToken);

        Assert.True(
            result.MigrationSucceeded,
            "This test expects MigrationSucceeded=false so the runner shows you the adversary output below.\n\n"
            + (result.FailureSummary ?? result.Exception?.ToString() ?? "(no FailureSummary or Exception)"));
    }

    /// <summary>
    /// Full chain: early steps must succeed; the last step fails — failure message shows hints for that migration.
    /// </summary>
    [Fact]
    public async Task RunAllAsync_last_step_red_test_prints_adversary_hints()
    {
        var steps = await MigrationAdversaryRunner.RunAllAsync(Batch, TestContext.Current.CancellationToken);

        Assert.Equal(9, steps.Count);

        for (int i = 0; i < 8; i++)
        {
            Assert.True(steps[i].Result.MigrationSucceeded, steps[i].Result.FailureSummary ?? steps[i].Result.Exception?.ToString());
        }

        var last = steps[8];
        Assert.Equal(HostileMigrationId, last.MigrationId);

        Assert.True(
            last.Result.MigrationSucceeded,
            "This test expects the unique-index migration to fail under adversary data. Output:\n\n"
            + (last.Result.FailureSummary ?? last.Result.Exception?.ToString() ?? "(no FailureSummary or Exception)"));
    }
}
