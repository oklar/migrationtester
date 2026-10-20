using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MigrationRepair.Planning;
using MigrationRepair.Runner;
using Xunit;

namespace MigrationRepair.Tests.SchemaCases;

/// <summary>
/// End-to-end runs over common schema shapes, each with its own hand-written migrations
/// (see <see cref="CreateDocuments"/> and friends): the adversary plants its data and the migration fails on it.
/// </summary>
[Collection("SqlServer integration")]
public sealed class SchemaCaseTests
{
    [Fact]
    public async Task Rowversion_column_does_not_break_seeding()
    {
        var result = await RunAsync<RowVersionContext>("20260101000002_AddUniqueDocumentTitle", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact]
    public async Task Guid_primary_key_does_not_break_duplicate_seeding()
    {
        var result = await RunAsync<GuidKeyContext>("20260101000002_AddUniqueAccountEmail", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact]
    public async Task Length_shrink_seeds_value_that_fits_old_column()
    {
        var result = await RunAsync<ShrinkLengthContext>("20260101000002_ShrinkProductCode", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.MaxLengthExceededAfterShrink);
    }

    [Fact]
    public async Task Unique_index_in_non_dbo_schema_is_exercised()
    {
        var result = await RunAsync<CustomSchemaContext>("20260101000002_AddUniqueAppItemCode", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint);
        Assert.Contains(result.SqlExecuted, sql => sql.Contains("[app].[Items]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Not_null_seed_keeps_required_foreign_key_valid()
    {
        var result = await RunAsync<RequiredForeignKeyContext>("20260101000002_MakeOrderNoteRequired", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.NonNullableColumnWithNull);
    }

    [Fact]
    public async Task Int_to_smallint_narrowing_is_exercised()
    {
        var result = await RunAsync<NarrowingContext>("20260101000002_NarrowQuantityToSmallint", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.ColumnTypeConversionIncompatible);
    }

    [Fact]
    public async Task Decimal_scale_increase_that_drops_integer_digits_is_exercised()
    {
        var result = await RunAsync<NarrowingContext>("20260101000003_IncreaseAmountScale", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.DecimalPrecisionOverflow);
    }

    [Fact]
    public async Task Same_column_gets_each_bad_value_in_its_own_row()
    {
        // Name becomes NOT NULL and shorter in one AlterColumn: the NULL and the long value need separate rows.
        var result = await RunAsync<RequiredAndShorterContext>("20260101000002_MakeNameRequiredAndShorter", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.NonNullableColumnWithNull);
        AssertBrokenByAdversary(result, AdversaryScenarioKind.MaxLengthExceededAfterShrink);
        Assert.Contains(result.SqlExecuted, sql => sql.StartsWith("UPDATE TOP (1) [dbo].[People] SET [Name] = NULL", StringComparison.Ordinal));
        Assert.Contains(result.SqlExecuted, sql => sql.StartsWith("INSERT INTO [dbo].[People]", StringComparison.Ordinal) && sql.Contains("REPLICATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Table_that_rejects_seed_rows_is_reported_instead_of_aborting_the_run()
    {
        // A CHECK constraint rejects the generated Status value, so Tickets gets no row at all.
        var steps = await MigrationAdversaryRunner.RunAllAsync(
            new MigrationAdversaryBatchOptions { CreateContext = () => new CheckedStatusContext(Options<CheckedStatusContext>()) },
            TestContext.Current.CancellationToken);

        Assert.Equal(2, steps.Count);
        var result = steps[1].Result;
        Assert.True(result.MigrationSucceeded, result.FailureSummary);
        Assert.Contains(result.Warnings, w => w.StartsWith("No baseline row in dbo.Tickets", StringComparison.Ordinal));

        var scenario = Assert.Single(result.PlannedScenarios);
        Assert.True(scenario.Skipped);
        Assert.StartsWith("Not seeded:", scenario.SkipReason);
        Assert.Contains("CK_Tickets_Status", scenario.SkipReason);
    }

    private static DbContextOptions<TContext> Options<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
            // Hand-written migrations have no model snapshot to compare against.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

    private static Task<MigrationAdversaryResult> RunAsync<TContext>(
        string targetMigrationId,
        Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var options = Options<TContext>();
        return MigrationAdversaryRunner.RunAsync(
            new MigrationAdversaryOptions
            {
                CreateContext = () => create(options),
                TargetMigrationId = targetMigrationId
            },
            TestContext.Current.CancellationToken);
    }

    private static void AssertBrokenByAdversary(MigrationAdversaryResult result, AdversaryScenarioKind expectedKind)
    {
        string planned = string.Join(
            ", ",
            result.PlannedScenarios.Select(static s => $"[{s.Kind}]{(s.Skipped ? $" skipped: {s.SkipReason}" : "")}"));

        Assert.False(
            result.MigrationSucceeded,
            $"Migration passed, but the adversarial data should have broken it. Planned: {(planned.Length == 0 ? "none" : planned)}");
        Assert.Contains(result.PlannedScenarios, s => s.Kind == expectedKind && !s.Skipped);
    }
}
