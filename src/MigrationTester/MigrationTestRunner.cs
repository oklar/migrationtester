using Microsoft.EntityFrameworkCore;
using MigrationTester.Runner;

namespace MigrationTester;

/// <summary>
/// Tests EF Core migrations against SQL Server with data that is valid before each migration but that the
/// migration's <c>Up()</c> may not handle. For each migration N it migrates a fresh test database to N-1,
/// plants that data, then migrates to N and reports whether, and on what, the migration failed.
/// </summary>
public static class MigrationTestRunner
{
    /// <summary>Tests every migration, or those in <see cref="MigrationTestOptions.MigrationIds"/>, in EF apply order.</summary>
    public static async Task<MigrationTestReport> RunAllAsync(
        MigrationTestOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MigrationIds is { Count: 0 })
        {
            throw new ArgumentException("MigrationIds, when set, must not be empty.", nameof(options));
        }

        return await RunStepsAsync(options, context => SelectMigrations(context, options.MigrationIds), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Tests the migrations from <paramref name="fromMigrationId"/> through <paramref name="toMigrationId"/>
    /// (inclusive, in EF apply order), e.g. to split a long history across CI jobs.
    /// Cannot be combined with <see cref="MigrationTestOptions.MigrationIds"/>.
    /// </summary>
    public static async Task<MigrationTestReport> RunRangeAsync(
        MigrationTestOptions options,
        string fromMigrationId,
        string toMigrationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(fromMigrationId);
        ArgumentException.ThrowIfNullOrEmpty(toMigrationId);
        if (options.MigrationIds is not null)
        {
            throw new ArgumentException("RunRangeAsync cannot be combined with MigrationTestOptions.MigrationIds.", nameof(options));
        }

        return await RunStepsAsync(options, context => SelectRange(context, fromMigrationId, toMigrationId), cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>Tests one migration (full id or name).</summary>
    public static async Task<MigrationStepResult> RunAsync(
        MigrationTestOptions options,
        string migrationId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(migrationId);

        var report = await RunStepsAsync(options, context => [MigrationReader.Resolve(context, migrationId)], cancellationToken)
            .ConfigureAwait(false);
        return report.Steps[0];
    }

    /// <summary>All migration ids in EF apply order, e.g. as data for one xUnit <c>[Theory]</c> case per migration.</summary>
    public static IReadOnlyList<string> GetMigrationIds(Func<DbContext> createContext)
    {
        ArgumentNullException.ThrowIfNull(createContext);
        using var context = createContext();
        return MigrationReader.GetMigrationIds(context);
    }

    private static async Task<MigrationTestReport> RunStepsAsync(
        MigrationTestOptions options,
        Func<DbContext, IReadOnlyList<string>> selectMigrations,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<string> migrationIds;
        TestDatabase database;
        await using (var context = options.CreateContext())
        {
            MigrationReader.EnsureSqlServer(context);
            migrationIds = selectMigrations(context);
            database = TestDatabase.For(context, options.DatabaseName);
        }

        var steps = new List<MigrationStepResult>(migrationIds.Count);
        var warnings = new List<string>();
        try
        {
            foreach (string id in migrationIds)
            {
                steps.Add(await StepRunner.RunAsync(options, database, id, cancellationToken).ConfigureAwait(false));
            }
        }
        finally
        {
            if (!options.KeepDatabase && await database.TryDropAsync(options.CreateContext).ConfigureAwait(false) is { } dropFailure)
            {
                warnings.Add($"Could not drop test database '{database.Name}': {dropFailure}");
            }
        }

        return new MigrationTestReport
        {
            Steps = steps,
            DatabaseName = database.Name,
            DatabaseKept = options.KeepDatabase,
            Warnings = warnings
        };
    }

    private static IReadOnlyList<string> SelectMigrations(DbContext context, IReadOnlyList<string>? wanted)
    {
        var all = MigrationReader.GetMigrationIds(context);
        if (wanted is null)
        {
            return all;
        }

        var selected = new HashSet<string>(StringComparer.Ordinal);
        foreach (string raw in wanted)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new ArgumentException("MigrationIds contains an empty entry.", nameof(wanted));
            }

            selected.Add(MigrationReader.Resolve(context, raw));
        }

        // EF apply order, not the order of MigrationIds.
        return [.. all.Where(selected.Contains)];
    }

    private static IReadOnlyList<string> SelectRange(DbContext context, string from, string to)
    {
        var all = MigrationReader.GetMigrationIds(context);
        string first = MigrationReader.Resolve(context, from);
        string last = MigrationReader.Resolve(context, to);
        int start = all.IndexOf(first);
        int end = all.IndexOf(last);
        if (start > end)
        {
            throw new ArgumentException($"RunRangeAsync: '{first}' comes after '{last}' in apply order.", nameof(from));
        }

        return all.GetRange(start, end - start + 1);
    }
}
