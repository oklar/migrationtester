using Microsoft.EntityFrameworkCore;
using MigrationTester.Planning;
using MigrationTester.Runner;

namespace MigrationTester.Examples;

/// <summary>
/// Demonstrates every adversarial scenario kind.
/// Each migration (02–08) fails because of a specific data-dependent problem.
/// Migration 01 succeeds (no prior data) and 09 succeeds (CHECK constraint is always skipped).
/// Run: <c>dotnet test --logger "console;verbosity=detailed"</c>
/// </summary>
[Collection("SqlServer scenario")]
public sealed class AdversaryFailureDemoTests
{
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
    /// Runs all 9 migrations in order. Migrations 02–08 each fail for a different adversarial
    /// scenario kind, while 01 (first migration) and 09 (check constraint, always skipped) succeed.
    /// </summary>
    [Fact]
    public async Task RunAllAsync_each_migration_triggers_its_adversarial_scenario()
    {
        var steps = await MigrationAdversaryRunner.RunAllAsync(Batch, TestContext.Current.CancellationToken);

        Assert.Equal(9, steps.Count);

        // Migration 01: first migration, no prior data → always succeeds.
        Assert.True(steps[0].Result.MigrationSucceeded,
            $"01_InitialSchema should succeed.\n{steps[0].Result.FailureSummary}");

        // Migration 02: ForeignKeyOrphan
        AssertFailedWithScenario(steps[1], AdversaryScenarioKind.ForeignKeyOrphan,
            "02 should fail: orphan Orders.CustomerId referencing missing Customers");

        // Migration 03: MaxLengthExceededAfterShrink
        AssertFailedWithScenario(steps[2], AdversaryScenarioKind.MaxLengthExceededAfterShrink,
            "03 should fail: Products.Code values longer than new max length 50");

        // Migration 04: DuplicateUniqueIndexOrConstraint
        AssertFailedWithScenario(steps[3], AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint,
            "04 should fail: duplicate Products.Code values prevent unique index creation");

        // Migration 05: NonNullableColumnWithNull
        AssertFailedWithScenario(steps[4], AdversaryScenarioKind.NonNullableColumnWithNull,
            "05 should fail: NULL Products.Name values prevent NOT NULL constraint");

        // Migration 06: NotNullColumnAddedWithoutDefault
        AssertFailedWithScenario(steps[5], AdversaryScenarioKind.NotNullColumnAddedWithoutDefault,
            "06 should fail: NOT NULL column Orders.Status added without default to table with rows");

        // Migration 07: ColumnTypeConversionIncompatible
        AssertFailedWithScenario(steps[6], AdversaryScenarioKind.ColumnTypeConversionIncompatible,
            "07 should fail: Tags.ExternalId contains non-GUID strings that can't convert to uniqueidentifier");

        // Migration 08: DecimalPrecisionOverflow
        AssertFailedWithScenario(steps[7], AdversaryScenarioKind.DecimalPrecisionOverflow,
            "08 should fail: Products.Price values overflow when precision shrinks to decimal(10,2)");

        // Migration 09: CheckConstraintViolation — always skipped (no automatic adversary seeding)
        Assert.True(steps[8].Result.MigrationSucceeded,
            $"09_AddCheckConstraintPositivePrice should succeed (CHECK scenario is always skipped).\n{steps[8].Result.FailureSummary}");
        Assert.Contains(steps[8].Result.PlannedScenarios,
            s => s.Kind == AdversaryScenarioKind.CheckConstraintViolation && s.Skipped);
    }

    /// <summary>
    /// Demonstrates running a single hostile migration and inspecting the failure output.
    /// Fails on purpose, so it only runs when requested: <c>dotnet test -- xUnit.Explicit=only</c>.
    /// </summary>
    [Fact(Explicit = true)]
    public async Task RunAsync_single_migration_shows_failure_summary_and_hints()
    {
        var result = await MigrationAdversaryRunner.RunAsync(
            new MigrationAdversaryOptions
            {
                CreateContext = Batch.CreateContext,
                TargetMigrationId = "20260322180300_04_AddUniqueProductCode"
            },
            TestContext.Current.CancellationToken);

        Assert.True(
            result.MigrationSucceeded,
            "This test intentionally fails to surface the adversary output:\n\n"
            + (result.FailureSummary ?? result.Exception?.ToString() ?? "(no output)"));
    }

    private static void AssertFailedWithScenario(
        MigrationStepResult step,
        AdversaryScenarioKind expectedKind,
        string context)
    {
        Assert.False(step.Result.MigrationSucceeded,
            $"{context}\n\nExpected failure but migration succeeded. "
            + $"Planned scenarios: {string.Join(", ", step.Result.PlannedScenarios.Select(s => $"[{s.Kind}]{(s.Skipped ? " SKIPPED" : "")}"))}");

        Assert.Contains(step.Result.PlannedScenarios,
            s => s.Kind == expectedKind && !s.Skipped);
    }
}
