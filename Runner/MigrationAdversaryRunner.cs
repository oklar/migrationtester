using System.Diagnostics;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using MigrationRepair.Capture;
using MigrationRepair.Hints;
using MigrationRepair.Planning;
using MigrationRepair.Schema;
using MigrationRepair.Seeding;

namespace MigrationRepair.Runner;

/// <summary>
/// SQL Server adversarial migration testing: for each target migration N, apply N−1, seed baseline + constraint-breaking rows from N’s <c>Up()</c>, then migrate to N.
/// Use <see cref="RunAllAsync"/> in integration tests to exercise the full history, or <see cref="RunRangeAsync"/> for a contiguous slice (e.g. CI sharding).
/// </summary>
public static class MigrationAdversaryRunner
{
    /// <summary>
    /// Runs the adversarial pipeline for every migration (or a filtered subset) in order.
    /// Each step uses a fresh <see cref="DbContext"/> and <c>EnsureDeleted</c>, same as calling <see cref="RunAsync"/> per id.
    /// </summary>
    public static async Task<IReadOnlyList<MigrationStepResult>> RunAllAsync(
        MigrationAdversaryBatchOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MigrationIds is { Count: 0 })
        {
            throw new ArgumentException("MigrationIds, when set, must not be empty.", nameof(options));
        }

        var ordered = ResolveMigrationsToRun(options);
        return await RunStepsAsync(options, ordered, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Runs the pipeline for each migration from <paramref name="fromMigrationIdInclusive"/> through
    /// <paramref name="toMigrationIdInclusive"/> in EF apply order (inclusive). Cannot be combined with <see cref="MigrationAdversaryBatchOptions.MigrationIds"/>.
    /// </summary>
    public static async Task<IReadOnlyList<MigrationStepResult>> RunRangeAsync(
        MigrationAdversaryBatchOptions options,
        string fromMigrationIdInclusive,
        string toMigrationIdInclusive,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrEmpty(fromMigrationIdInclusive);
        ArgumentException.ThrowIfNullOrEmpty(toMigrationIdInclusive);

        if (options.MigrationIds is not null)
        {
            throw new InvalidOperationException(
                "RunRangeAsync cannot be used when MigrationAdversaryBatchOptions.MigrationIds is set; use RunAllAsync with MigrationIds or RunRangeAsync alone.");
        }

        using var context = options.CreateContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var all = context.Database.GetMigrations().ToList();
        var fromResolved = assembly.FindMigrationId(fromMigrationIdInclusive);
        var toResolved = assembly.FindMigrationId(toMigrationIdInclusive);
        if (string.IsNullOrEmpty(fromResolved) || string.IsNullOrEmpty(toResolved))
        {
            throw new InvalidOperationException(
                $"RunRangeAsync: could not resolve migration id(s). From: '{fromMigrationIdInclusive}' -> '{fromResolved}', To: '{toMigrationIdInclusive}' -> '{toResolved}'.");
        }

        var i0 = all.IndexOf(fromResolved);
        var i1 = all.IndexOf(toResolved);
        if (i0 < 0 || i1 < 0)
        {
            throw new InvalidOperationException(
                $"RunRangeAsync: migration not in apply order. From '{fromResolved}' index {i0}, To '{toResolved}' index {i1}.");
        }

        if (i0 > i1)
        {
            throw new InvalidOperationException($"RunRangeAsync: from '{fromResolved}' must not be after to '{toResolved}'.");
        }

        var slice = all.GetRange(i0, i1 - i0 + 1);
        return await RunStepsAsync(options, slice, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Returns true when every step succeeded (convenience for integration tests). Vacuously true for an empty list.</summary>
    public static bool AllStepsSucceeded(IReadOnlyList<MigrationStepResult> steps)
        => steps.All(static s => s.Result.MigrationSucceeded);

    /// <summary>First failing step, or null when all succeeded or the list is empty.</summary>
    public static MigrationStepResult? FirstFailedStep(IReadOnlyList<MigrationStepResult> steps)
        => steps.FirstOrDefault(static s => !s.Result.MigrationSucceeded);

    public static async Task<MigrationAdversaryResult> RunAsync(
        MigrationAdversaryOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var context = options.CreateContext();
        try
        {
            return await RunCoreAsync(context, options, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            context.Dispose();
        }
    }

    private static IReadOnlyList<string> ResolveMigrationsToRun(MigrationAdversaryBatchOptions options)
    {
        using var context = options.CreateContext();
        var all = context.Database.GetMigrations().ToList();
        if (options.MigrationIds is null)
        {
            return all;
        }

        var assembly = context.GetService<IMigrationsAssembly>();
        var wanted = new List<string>();
        var idSet = new HashSet<string>(StringComparer.Ordinal);
        foreach (var raw in options.MigrationIds)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                throw new ArgumentException("MigrationIds contains an empty entry.", nameof(options));
            }

            var resolved = assembly.FindMigrationId(raw);
            if (string.IsNullOrEmpty(resolved))
            {
                throw new InvalidOperationException($"Migration '{raw}' not found.");
            }

            if (idSet.Add(resolved))
            {
                wanted.Add(resolved);
            }
        }

        var wantedSet = new HashSet<string>(wanted, StringComparer.Ordinal);
        var filtered = all.Where(m => wantedSet.Contains(m)).ToList();
        if (filtered.Count != wanted.Count)
        {
            throw new InvalidOperationException(
                "One or more resolved MigrationIds are not present in the EF migrations list for this context.");
        }

        // Preserve EF order, not the order in options.MigrationIds
        return filtered;
    }

    private static async Task<IReadOnlyList<MigrationStepResult>> RunStepsAsync(
        MigrationAdversaryBatchOptions batch,
        IReadOnlyList<string> migrationIds,
        CancellationToken cancellationToken)
    {
        var results = new List<MigrationStepResult>(migrationIds.Count);
        foreach (var id in migrationIds)
        {
            var sw = Stopwatch.StartNew();
            var single = new MigrationAdversaryOptions
            {
                CreateContext = batch.CreateContext,
                TargetMigrationId = id,
                ActiveProvider = batch.ActiveProvider
            };

            var context = batch.CreateContext();
            try
            {
                var result = await RunCoreAsync(context, single, cancellationToken).ConfigureAwait(false);
                sw.Stop();
                results.Add(new MigrationStepResult
                {
                    MigrationId = id,
                    Result = result,
                    Duration = sw.Elapsed
                });
            }
            finally
            {
                context.Dispose();
            }
        }

        return results;
    }

    private static async Task<MigrationAdversaryResult> RunCoreAsync(
        DbContext context,
        MigrationAdversaryOptions options,
        CancellationToken cancellationToken)
    {
        var assembly = context.GetService<IMigrationsAssembly>();
        var ordered = context.Database.GetMigrations().ToList();
        var target = assembly.FindMigrationId(options.TargetMigrationId);
        if (string.IsNullOrEmpty(target))
        {
            throw new InvalidOperationException($"Migration '{options.TargetMigrationId}' not found.");
        }

        var idx = ordered.IndexOf(target);
        if (idx < 0)
        {
            throw new InvalidOperationException($"Migration '{options.TargetMigrationId}' not found (resolved: '{target}').");
        }

        var prev = idx > 0 ? ordered[idx - 1] : null;
        var activeProvider = options.ActiveProvider ?? ActiveProviderResolver.Resolve(context);

        var migrationType = assembly.Migrations[target];
        object? instance;
        try
        {
            instance = Activator.CreateInstance(migrationType);
        }
        catch (Exception ex) when (ex is not InvalidOperationException)
        {
            throw new InvalidOperationException(
                $"Could not create migration instance '{migrationType.Name}'. EF migrations must have an accessible parameterless constructor (use partial class + designer pattern).",
                ex);
        }

        if (instance is null)
        {
            throw new InvalidOperationException($"Activator returned null for migration type '{migrationType.Name}'.");
        }

        var migration = (Migration)instance;

        var ops = MigrationOperationCapture.Capture(migration, activeProvider);
        var hasRawSql = MigrationOperationCapture.ContainsRawSql(ops);
        var warnings = new List<string>();
        if (hasRawSql)
        {
            warnings.Add(
                "This migration contains migrationBuilder.Sql(...). Automatic scenario coverage may be incomplete (false negatives possible).");
        }

        await context.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);

        if (prev is not null)
        {
            await context.Database.MigrateAsync(prev, cancellationToken).ConfigureAwait(false);
        }

        // First migration: database may not exist until Migrate(target); N-1 has no user tables.
        var schema = prev is null
            ? RelationalSchemaModel.Empty
            : await RelationalSchemaIntrospector.LoadAsync(context.Database.GetDbConnection(), cancellationToken)
                .ConfigureAwait(false);

        var tables = new HashSet<string>(schema.TableNames, StringComparer.OrdinalIgnoreCase);
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        var sqlLog = new List<string>();
        foreach (var sql in SeedSqlGenerator.BaselineInserts(schema))
        {
            await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            sqlLog.Add(sql);
        }

        foreach (var scenario in scenarios.Where(static s => !s.Skipped))
        {
            foreach (var sql in SeedSqlGenerator.AdversarialStatements(scenario, schema))
            {
                await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
                sqlLog.Add(sql);
            }
        }

        try
        {
            await context.Database.MigrateAsync(target, cancellationToken).ConfigureAwait(false);
            return new MigrationAdversaryResult
            {
                MigrationSucceeded = true,
                Warnings = warnings,
                PlannedScenarios = scenarios,
                SqlExecuted = sqlLog,
                ContainsRawSqlInMigration = hasRawSql
            };
        }
        catch (Exception ex)
        {
            var hint = BuildHints(scenarios.Where(static s => !s.Skipped));
            return new MigrationAdversaryResult
            {
                MigrationSucceeded = false,
                Exception = ex,
                FailureSummary = FormatFailure(ex, scenarios, hint),
                Warnings = warnings,
                PlannedScenarios = scenarios,
                SqlExecuted = sqlLog,
                ContainsRawSqlInMigration = hasRawSql
            };
        }
    }

    private static string BuildHints(IEnumerable<AdversaryScenario> executed)
    {
        var sb = new StringBuilder();
        foreach (var s in executed)
        {
            sb.AppendLine($"-- {s.Name}");
            sb.AppendLine(RepairHintCatalog.ForScenario(s));
            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    private static string FormatFailure(Exception ex, IReadOnlyList<AdversaryScenario> all, string hints)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Migration Up() failed with pre-migration-compatible data.");
        sb.AppendLine($"Exception: {ex.Message}");
        sb.AppendLine();
        sb.AppendLine("Relevant adversarial scenarios (non-skipped):");
        foreach (var s in all.Where(static x => !x.Skipped))
        {
            sb.AppendLine($" - [{s.Kind}] {s.Name}");
        }

        sb.AppendLine();
        sb.AppendLine("SQL repair hints:");
        sb.AppendLine(hints);
        return sb.ToString();
    }
}
