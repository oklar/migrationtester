using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace MigrationTester.Tests.SchemaCases;

/// <summary>
/// End-to-end runs over common schema shapes, each with its own hand-written migrations
/// (see <see cref="CreateDocuments"/> and friends): the data is planted and the migration fails on it.
/// </summary>
[Collection("SqlServer integration")]
public sealed class SchemaCaseTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Rowversion_column_does_not_break_seeding()
    {
        var step = await RunAsync<RowVersionContext>("20260101000002_AddUniqueDocumentTitle", o => new(o));

        AssertBrokenBy(step, ScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact]
    public async Task Guid_primary_key_does_not_break_duplicate_seeding()
    {
        var step = await RunAsync<GuidKeyContext>("20260101000002_AddUniqueAccountEmail", o => new(o));

        AssertBrokenBy(step, ScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact]
    public async Task Length_shrink_seeds_value_that_fits_old_column()
    {
        var step = await RunAsync<ShrinkLengthContext>("20260101000002_ShrinkProductCode", o => new(o));

        AssertBrokenBy(step, ScenarioKind.MaxLengthExceededAfterShrink);
    }

    [Fact]
    public async Task Unique_index_in_non_dbo_schema_is_exercised()
    {
        var step = await RunAsync<CustomSchemaContext>("20260101000002_AddUniqueAppItemCode", o => new(o));

        AssertBrokenBy(step, ScenarioKind.DuplicateUniqueIndexOrConstraint);
        Assert.Contains(step.SqlExecuted, sql => sql.Contains("[app].[Items]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Not_null_seed_keeps_required_foreign_key_valid()
    {
        var step = await RunAsync<RequiredForeignKeyContext>("20260101000002_MakeOrderNoteRequired", o => new(o));

        AssertBrokenBy(step, ScenarioKind.NonNullableColumnWithNull);
    }

    [Fact]
    public async Task Int_to_smallint_narrowing_is_exercised()
    {
        var step = await RunAsync<NarrowingContext>("20260101000002_NarrowQuantityToSmallint", o => new(o));

        AssertBrokenBy(step, ScenarioKind.ColumnTypeConversionIncompatible);
    }

    [Fact]
    public async Task Decimal_scale_increase_that_drops_integer_digits_is_exercised()
    {
        var step = await RunAsync<NarrowingContext>("20260101000003_IncreaseAmountScale", o => new(o));

        AssertBrokenBy(step, ScenarioKind.DecimalPrecisionOverflow);
    }

    [Fact]
    public async Task Filtered_unique_index_on_nullable_column_is_exercised()
    {
        var step = await RunAsync<FilteredUniqueContext>("20260101000002_AddUniqueUserEmail", o => new(o));

        AssertBrokenBy(step, ScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact]
    public async Task Filtered_unique_index_on_optional_foreign_key_is_exercised()
    {
        // N-1 already has the filtered unique index on Email, so the copies also need different emails.
        var step = await RunAsync<FilteredUniqueContext>("20260101000003_AddUniqueUserProfile", o => new(o));

        AssertBrokenBy(step, ScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact]
    public async Task Same_column_gets_each_bad_value_in_its_own_row()
    {
        // Name becomes NOT NULL and shorter in one AlterColumn: the NULL and the long value need separate rows.
        var step = await RunAsync<RequiredAndShorterContext>("20260101000002_MakeNameRequiredAndShorter", o => new(o));

        Assert.Equal(MigrationStepOutcome.Failed, step.Outcome);
        Assert.All(step.Scenarios, s => Assert.Equal(ScenarioStatus.Planted, s.Status));
        Assert.NotEmpty(step.FailingScenarios);
        Assert.Contains(step.SqlExecuted, sql => sql.StartsWith("UPDATE TOP (1) [dbo].[People] SET [Name] = NULL", StringComparison.Ordinal));
        Assert.Contains(step.SqlExecuted, sql => sql.StartsWith("INSERT INTO [dbo].[People]", StringComparison.Ordinal) && sql.Contains("REPLICATE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Table_that_rejects_seed_rows_is_reported_instead_of_aborting_the_run()
    {
        // A CHECK constraint rejects the generated Status value, so Tickets gets no row at all.
        var report = await MigrationTestRunner.RunAllAsync(Options<CheckedStatusContext>(o => new(o)), Ct);

        Assert.Equal(2, report.Steps.Count);
        var step = report.Steps[1];
        Assert.Equal(MigrationStepOutcome.Passed, step.Outcome);
        Assert.True(report.Succeeded, report.ToString());
        Assert.Contains(step.Warnings, w => w.StartsWith("No baseline row in dbo.Tickets", StringComparison.Ordinal));

        var scenario = Assert.Single(step.Scenarios);
        Assert.Equal(ScenarioStatus.NotSeeded, scenario.Status);
        Assert.Contains("CK_Tickets_Status", scenario.Reason);
        Assert.Contains("Not seeded (not tested):", step.Summary, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RequireAllScenariosSeeded_fails_a_step_with_unseeded_scenarios()
    {
        var options = Options<CheckedStatusContext>(o => new(o));
        var strict = new MigrationTestOptions { CreateContext = options.CreateContext, RequireAllScenariosSeeded = true };

        var report = await MigrationTestRunner.RunAllAsync(strict, Ct);

        Assert.Equal(MigrationStepOutcome.Passed, report.Steps[1].Outcome);
        Assert.False(report.Steps[1].Succeeded);
        Assert.False(report.Succeeded);
        Assert.Throws<MigrationTestException>(report.ThrowIfFailed);
    }

    [Fact]
    public async Task Seed_rows_do_not_take_ids_that_HasData_rows_use()
    {
        // The next identity would be 3 and the free integer key 2; a later migration inserts exactly those.
        var step = await RunAsync<SeedDataContext>("20260101000002_AddMoreSeedRows", o => new(o));

        Assert.True(step.Outcome == MigrationStepOutcome.Passed, step.Summary);
        Assert.DoesNotContain(step.Warnings, w => w.StartsWith("No baseline row", StringComparison.Ordinal));
        Assert.Contains(step.SqlExecuted, sql => sql.Contains("RESEED, 1000000", StringComparison.Ordinal));
        Assert.Contains(step.SqlExecuted, sql => sql.StartsWith("INSERT INTO [dbo].[Countries] ([Id], [Name]) VALUES (1000001,", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Statement_outside_a_transaction_plants_for_real_and_the_next_step_starts_clean()
    {
        var report = await MigrationTestRunner.RunAllAsync(Options<OwnTransactionContext>(o => new(o)), Ct);

        Assert.Equal(
            [MigrationStepOutcome.Passed, MigrationStepOutcome.Failed, MigrationStepOutcome.Failed],
            report.Steps.Select(static s => s.Outcome));
        AssertBrokenBy(report.Steps[1], ScenarioKind.NonNullableColumnWithNull);
        AssertBrokenBy(report.Steps[2], ScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact]
    public async Task Broken_earlier_migration_is_an_error_for_the_next_step_not_an_exception()
    {
        var report = await MigrationTestRunner.RunAllAsync(Options<BrokenFirstContext>(o => new(o)), Ct);

        Assert.Equal([MigrationStepOutcome.Failed, MigrationStepOutcome.Error], report.Steps.Select(static s => s.Outcome));
        Assert.Contains("No scenarios were planted", report.Steps[0].Summary, StringComparison.Ordinal);
        Assert.Contains("Migrating the test database to the previous migration", report.Steps[1].Summary, StringComparison.Ordinal);
        Assert.Contains("SQL Server error 50000", report.Steps[1].Summary, StringComparison.Ordinal);
    }

    private static MigrationTestOptions Options<TContext>(Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
            // Hand-written migrations have no model snapshot to compare against.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
        return new MigrationTestOptions { CreateContext = () => create(options) };
    }

    private static Task<MigrationStepResult> RunAsync<TContext>(
        string migrationId,
        Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
        => MigrationTestRunner.RunAsync(Options(create), migrationId, Ct);

    private static void AssertBrokenBy(MigrationStepResult step, ScenarioKind expectedKind)
    {
        Assert.True(step.Outcome == MigrationStepOutcome.Failed, $"Expected the planted data to break the migration.\n{step.Summary}");
        Assert.Contains(step.Scenarios, s => s.Kind == expectedKind && s.Status == ScenarioStatus.Planted);
        Assert.True(step.FailingScenarios.Any(s => s.Kind == expectedKind), $"Expected the failure to be matched to {expectedKind}.\n{step.Summary}");
    }
}
