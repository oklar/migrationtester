using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using MigrationRepair.Planning;
using MigrationRepair.Runner;
using Xunit;

namespace MigrationRepair.Tests.KnownIssues;

/// <summary>
/// Common schemas the adversary currently gets wrong. Each test asserts the behavior we want:
/// the run completes, the bad data is planted, and the migration fails on it. They fail today
/// (seeding crashes, or nothing is planted), so they are <c>Explicit</c> and stay out of the
/// default run. Remove <c>Explicit</c> from a test once its case is fixed.
/// Run them with <c>dotnet test tests/MigrationRepair.Tests -- xUnit.Explicit=only</c>.
/// </summary>
[Collection("SqlServer integration")]
public sealed class KnownIssueTests
{
    [Fact(Explicit = true)]
    public async Task Rowversion_column_does_not_break_seeding()
    {
        // Today: baseline INSERT writes an explicit value into the rowversion column.
        var result = await RunAsync<RowVersionContext>("20260101000002_AddUniqueDocumentTitle", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact(Explicit = true)]
    public async Task Guid_primary_key_does_not_break_duplicate_seeding()
    {
        // Today: the duplicate row reuses the baseline row's GUID primary key.
        var result = await RunAsync<GuidKeyContext>("20260101000002_AddUniqueAccountEmail", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact(Explicit = true)]
    public async Task Length_shrink_seeds_value_that_fits_old_column()
    {
        // Today: the long value is 120 chars, which the old nvarchar(100) cannot hold either.
        var result = await RunAsync<ShrinkLengthContext>("20260101000002_ShrinkProductCode", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.MaxLengthExceededAfterShrink);
    }

    [Fact(Explicit = true)]
    public async Task Unique_index_in_non_dbo_schema_is_exercised()
    {
        // Today: introspection reads dbo only, so the scenario is skipped and the run passes.
        var result = await RunAsync<CustomSchemaContext>("20260101000002_AddUniqueAppItemCode", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint);
    }

    [Fact(Explicit = true)]
    public async Task Not_null_seed_keeps_required_foreign_key_valid()
    {
        // Today: the NULL row is a new INSERT whose CustomerId points at no customer.
        var result = await RunAsync<RequiredForeignKeyContext>("20260101000002_MakeOrderNoteRequired", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.NonNullableColumnWithNull);
    }

    [Fact(Explicit = true)]
    public async Task Int_to_smallint_narrowing_is_exercised()
    {
        // Today: same type family, so no scenario is planned.
        var result = await RunAsync<NarrowingContext>("20260101000002_NarrowQuantityToSmallint", o => new(o));

        AssertBrokenByAdversary(result, expectedKind: null);
    }

    [Fact(Explicit = true)]
    public async Task Decimal_scale_increase_that_drops_integer_digits_is_exercised()
    {
        // Today: only a precision decrease is detected; equal precision with a larger scale is not.
        var result = await RunAsync<NarrowingContext>("20260101000003_IncreaseAmountScale", o => new(o));

        AssertBrokenByAdversary(result, AdversaryScenarioKind.DecimalPrecisionOverflow);
    }

    private static Task<MigrationAdversaryResult> RunAsync<TContext>(
        string targetMigrationId,
        Func<DbContextOptions<TContext>, TContext> create)
        where TContext : DbContext
    {
        var options = new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
            // Hand-written migrations have no model snapshot to compare against.
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;

        return MigrationAdversaryRunner.RunAsync(
            new MigrationAdversaryOptions
            {
                CreateContext = () => create(options),
                TargetMigrationId = targetMigrationId
            },
            TestContext.Current.CancellationToken);
    }

    private static void AssertBrokenByAdversary(MigrationAdversaryResult result, AdversaryScenarioKind? expectedKind)
    {
        string planned = string.Join(
            ", ",
            result.PlannedScenarios.Select(static s => $"[{s.Kind}]{(s.Skipped ? $" skipped: {s.SkipReason}" : "")}"));

        Assert.False(
            result.MigrationSucceeded,
            $"Migration passed, but the adversarial data should have broken it. Planned: {(planned.Length == 0 ? "none" : planned)}");

        if (expectedKind is { } kind)
        {
            Assert.Contains(result.PlannedScenarios, s => s.Kind == kind && !s.Skipped);
        }
    }
}
