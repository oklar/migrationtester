using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Planning;
using MigrationTester.Schema;
using MigrationTester.Seeding;

namespace MigrationTester.Runner;

/// <summary>
/// One migration N: fresh database, migrate to N-1, plant data from N's <c>Up()</c>, migrate to N.
/// Problems before the migration runs become <see cref="MigrationStepOutcome.Error"/> instead of exceptions.
/// </summary>
internal static class StepRunner
{
    public static async Task<MigrationStepResult> RunAsync(
        MigrationTestOptions options,
        TestDatabase database,
        string migrationId,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var context = database.CreateContext(options.CreateContext);

        var all = MigrationReader.GetMigrationIds(context);
        int index = all.IndexOf(migrationId);
        string? previous = index > 0 ? all[index - 1] : null;

        MigrationStepResult Result(
            MigrationStepOutcome outcome,
            Exception? exception = null,
            string? errorStage = null,
            IReadOnlyList<MigrationScenario>? scenarios = null,
            IReadOnlyList<MigrationScenario>? failing = null,
            IReadOnlyList<string>? warnings = null,
            IReadOnlyList<string>? sql = null,
            bool rawSql = false) => new()
            {
                MigrationId = migrationId,
                Outcome = outcome,
                Exception = exception,
                ErrorStage = errorStage,
                Scenarios = scenarios ?? [],
                FailingScenarios = failing ?? [],
                Warnings = warnings ?? [],
                SqlExecuted = sql ?? [],
                ContainsRawSql = rawSql,
                DatabaseName = database.Name,
                Duration = stopwatch.Elapsed,
                RequireAllScenariosSeeded = options.RequireAllScenariosSeeded
            };

        IReadOnlyList<MigrationOperation> operations;
        try
        {
            operations = MigrationReader.UpOperations(context, migrationId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(MigrationStepOutcome.Error, ex, $"Reading the operations of '{migrationId}' failed");
        }

        bool rawSql = operations.Any(static o => o is SqlOperation);
        var warnings = new List<string>();
        if (rawSql)
        {
            warnings.Add(
                "This migration contains migrationBuilder.Sql(...). Its effects are not analyzed, so coverage may be incomplete (false negatives possible).");
        }

        IReadOnlyList<MigrationScenario> scenarios;
        IReadOnlyList<string> sqlLog;
        string stage = "Creating the test database";
        try
        {
            await context.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
            if (previous is null)
            {
                // First migration: no tables exist before it.
                scenarios = ScenarioPlanner.Plan(operations, RelationalSchemaModel.Empty);
                sqlLog = [];
            }
            else
            {
                stage = $"Migrating an empty database to the previous migration '{previous}'";
                await context.Database.MigrateAsync(previous, cancellationToken).ConfigureAwait(false);

                stage = "Reading and seeding the database at the previous migration";
                var seed = await SeedAsync(context, operations, cancellationToken).ConfigureAwait(false);
                scenarios = seed.Scenarios;
                sqlLog = seed.SqlExecuted;
                warnings.AddRange(seed.Warnings);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(MigrationStepOutcome.Error, ex, stage, warnings: warnings, rawSql: rawSql);
        }

        try
        {
            await context.Database.MigrateAsync(migrationId, cancellationToken).ConfigureAwait(false);
            return Result(MigrationStepOutcome.Passed, scenarios: scenarios, warnings: warnings, sql: sqlLog, rawSql: rawSql);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(
                MigrationStepOutcome.Failed,
                ex,
                scenarios: scenarios,
                failing: FailureAnalyzer.FailingScenarios(ex, scenarios),
                warnings: warnings,
                sql: sqlLog,
                rawSql: rawSql);
        }
    }

    private static async Task<SeedResult> SeedAsync(
        DbContext context,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken)
    {
        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = context.Database.GetDbConnection();
            var schema = await RelationalSchemaIntrospector.LoadAsync(
                    connection,
                    relational.MigrationsHistoryTableSchema,
                    relational.MigrationsHistoryTableName ?? HistoryRepository.DefaultTableName,
                    cancellationToken)
                .ConfigureAwait(false);

            return await AdversarySeeder.SeedAsync(connection, schema, ScenarioPlanner.Plan(operations, schema), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }
}
