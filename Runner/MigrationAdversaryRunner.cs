using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationRepair.Capture;
using MigrationRepair.Hints;
using MigrationRepair.Planning;
using MigrationRepair.Schema;
using MigrationRepair.Fuzzing;
using MigrationRepair.Seeding;

namespace MigrationRepair.Runner;

/// <summary>
/// Applies migration N-1, seeds baseline + adversarial data, then migrates to N.
/// </summary>
public static class MigrationAdversaryRunner
{
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
        var family = options.DatabaseFamily == DatabaseFamily.Portable
            ? ActiveProviderResolver.DetectFamily(activeProvider)
            : options.DatabaseFamily;

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
        if (family == DatabaseFamily.Sqlite)
        {
            SqliteConnection.ClearAllPools();
        }

        if (prev is not null)
        {
            await context.Database.MigrateAsync(prev, cancellationToken).ConfigureAwait(false);
        }

        RelationalSchemaModel schema;
        if (family == DatabaseFamily.MySql)
        {
            var conn = context.Database.GetDbConnection();
            schema = await RelationalSchemaIntrospector.LoadAsync(conn, DatabaseFamily.MySql, cancellationToken)
                .ConfigureAwait(false);
        }
        else
        {
            var cs = context.Database.GetConnectionString();
            if (string.IsNullOrWhiteSpace(cs))
            {
                throw new InvalidOperationException(
                    "DbContext has no connection string; cannot introspect the database for seeding.");
            }

            schema = await RelationalSchemaIntrospector.LoadSqliteFromConnectionStringAsync(cs, cancellationToken)
                .ConfigureAwait(false);
        }

        var tables = new HashSet<string>(schema.TableNames, StringComparer.OrdinalIgnoreCase);
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        var sqlLog = new List<string>();
        foreach (var sql in SeedSqlGenerator.BaselineInserts(schema, family))
        {
            await context.Database.ExecuteSqlRawAsync(sql, cancellationToken).ConfigureAwait(false);
            sqlLog.Add(sql);
        }

        if (options.EnableFuzzing)
        {
            await MigrationFuzzer.RunAsync(context, schema, family, options.FuzzSeed, options.FuzzIterationCount, sqlLog, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var scenario in scenarios.Where(static s => !s.Skipped))
        {
            foreach (var sql in SeedSqlGenerator.AdversarialStatements(scenario, schema, family))
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
            var hint = BuildHints(scenarios.Where(static s => !s.Skipped), family);
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

    private static string BuildHints(IEnumerable<AdversaryScenario> executed, DatabaseFamily family)
    {
        var sb = new StringBuilder();
        foreach (var s in executed)
        {
            sb.AppendLine($"-- {s.Name}");
            sb.AppendLine(RepairHintCatalog.ForScenario(s, family));
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
