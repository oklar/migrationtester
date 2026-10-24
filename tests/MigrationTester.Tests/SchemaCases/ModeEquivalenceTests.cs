using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace MigrationTester.Tests.SchemaCases;

/// <summary>
/// The default mode (planted run rolled back, then applied clean) must report exactly what rebuilding the
/// database from empty for every migration reports.
/// </summary>
[Collection("SqlServer integration")]
public sealed class ModeEquivalenceTests
{
    private static readonly Dictionary<string, Func<DbContext>> Contexts = new()
    {
        ["TestDbContext"] = () => new TestDbContext(Build<TestDbContext>()),
        ["RowVersion"] = () => new RowVersionContext(Build<RowVersionContext>()),
        ["GuidKey"] = () => new GuidKeyContext(Build<GuidKeyContext>()),
        ["ShrinkLength"] = () => new ShrinkLengthContext(Build<ShrinkLengthContext>()),
        ["CustomSchema"] = () => new CustomSchemaContext(Build<CustomSchemaContext>()),
        ["RequiredForeignKey"] = () => new RequiredForeignKeyContext(Build<RequiredForeignKeyContext>()),
        ["Narrowing"] = () => new NarrowingContext(Build<NarrowingContext>()),
        ["FilteredUnique"] = () => new FilteredUniqueContext(Build<FilteredUniqueContext>()),
        ["RequiredAndShorter"] = () => new RequiredAndShorterContext(Build<RequiredAndShorterContext>()),
        ["CheckedStatus"] = () => new CheckedStatusContext(Build<CheckedStatusContext>()),
        ["SeedData"] = () => new SeedDataContext(Build<SeedDataContext>()),
        ["OwnTransaction"] = () => new OwnTransactionContext(Build<OwnTransactionContext>()),
        ["BrokenFirst"] = () => new BrokenFirstContext(Build<BrokenFirstContext>())
    };

    public static TheoryData<string> Cases => new(Contexts.Keys);

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Transactional_mode_reports_the_same_as_rebuilding(string name)
    {
        var fast = await MigrationTestRunner.RunAllAsync(
            new MigrationTestOptions { CreateContext = Contexts[name] }, TestContext.Current.CancellationToken);
        var rebuilt = await MigrationTestRunner.RunAllAsync(
            new MigrationTestOptions { CreateContext = Contexts[name], RebuildDatabaseForEachMigration = true },
            TestContext.Current.CancellationToken);

        Assert.Equal(Describe(rebuilt), Describe(fast));
    }

    private static List<string> Describe(MigrationTestReport report) =>
    [
        .. report.Steps.Select(static s =>
            $"{s.MigrationId}: {s.Outcome}; "
            + string.Join(", ", s.Scenarios.Select(static c => $"{c.Kind}={c.Status}"))
            + " | failing: " + string.Join(", ", s.FailingScenarios.Select(static c => c.Kind)))
    ];

    private static DbContextOptions<TContext> Build<TContext>()
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseSqlServer(TestSqlServerConfig.TestRunConnectionString)
            .ConfigureWarnings(w => w.Ignore(RelationalEventId.PendingModelChangesWarning))
            .Options;
}
