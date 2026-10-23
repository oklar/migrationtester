using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Planning;
using MigrationTester.Schema;
using MigrationTester.Seeding;

namespace MigrationTester.Runner;

/// <summary>
/// One migration N: bring the database to a clean N-1, plant data from N's <c>Up()</c>, run N on it.
/// <para>
/// By default the planting and N run inside one transaction that is rolled back, after which N is applied
/// for real to the clean database, so the next step starts from a clean N without rebuilding. Migrations with
/// statements that cannot run in a transaction, the last step under <see cref="MigrationTestOptions.KeepDatabase"/>,
/// and <see cref="MigrationTestOptions.RebuildDatabaseForEachMigration"/> plant and migrate for real instead.
/// </para>
/// Problems before the migration runs become <see cref="MigrationStepOutcome.Error"/> instead of exceptions.
/// </summary>
internal static class StepRunner
{
    public static async Task<MigrationStepResult> RunAsync(
        MigrationTestOptions options,
        TestDatabase database,
        string migrationId,
        bool isLastStep,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        await using var context = database.CreateContext(options.CreateContext);

        var all = MigrationReader.GetMigrationIds(context);
        int index = all.IndexOf(migrationId);
        string? previous = index > 0 ? all[index - 1] : null;
        var warnings = new List<string>();
        bool rawSql = false;

        MigrationStepResult Result(
            MigrationStepOutcome outcome,
            Exception? exception = null,
            string? errorStage = null,
            IReadOnlyList<MigrationScenario>? scenarios = null,
            IReadOnlyList<string>? sql = null) => new()
            {
                MigrationId = migrationId,
                Outcome = outcome,
                Exception = exception,
                ErrorStage = errorStage,
                Scenarios = scenarios ?? [],
                FailingScenarios = outcome == MigrationStepOutcome.Failed && exception is not null && scenarios is not null
                    ? FailureAnalyzer.FailingScenarios(exception, scenarios)
                    : [],
                Warnings = warnings,
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
            return Result(MigrationStepOutcome.Error, ex, $"Reading the operations of '{migrationId}'");
        }

        rawSql = operations.Any(static o => o is SqlOperation);
        if (rawSql)
        {
            warnings.Add(
                "This migration contains migrationBuilder.Sql(...). Its effects are not analyzed, so coverage may be incomplete (false negatives possible).");
        }

        string stage = previous is null
            ? "Creating the test database"
            : $"Migrating the test database to the previous migration '{previous}'";
        try
        {
            await PrepareAsync(context, database, all, previous, options.RebuildDatabaseForEachMigration, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            database.CleanAt = null;
            return Result(MigrationStepOutcome.Error, ex, stage);
        }

        if (previous is null)
        {
            // First migration: no tables exist before it, so nothing is planted and the database stays clean.
            var none = ScenarioPlanner.Plan(operations, RelationalSchemaModel.Empty);
            try
            {
                await context.Database.MigrateAsync(migrationId, cancellationToken).ConfigureAwait(false);
                database.CleanAt = migrationId;
                return Result(MigrationStepOutcome.Passed, scenarios: none);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                database.CleanAt = null;
                return Result(MigrationStepOutcome.Failed, ex, scenarios: none);
            }
        }

        stage = "Reading and seeding the database at the previous migration";
        bool useTransaction = !options.RebuildDatabaseForEachMigration
            && !(isLastStep && options.KeepDatabase)
            && !NeedsItsOwnTransaction(operations);
        if (useTransaction)
        {
            TransactionalRun? run;
            try
            {
                run = await RunInTransactionAsync(context, previous, migrationId, operations, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                database.CleanAt = null;
                return Result(MigrationStepOutcome.Error, ex, stage);
            }

            if (run is not null)
            {
                warnings.AddRange(run.Seed.Warnings);
                if (!isLastStep)
                {
                    await AdvanceAsync(context, database, migrationId, warnings, cancellationToken).ConfigureAwait(false);
                }

                return run.Failure is null
                    ? Result(MigrationStepOutcome.Passed, scenarios: run.Seed.Scenarios, sql: run.Seed.SqlExecuted)
                    : Result(MigrationStepOutcome.Failed, run.Failure, scenarios: run.Seed.Scenarios, sql: run.Seed.SqlExecuted);
            }

            // A seed error ended the transaction, so some planted data may be committed: start over for real.
            try
            {
                database.CleanAt = null;
                await PrepareAsync(context, database, all, previous, rebuild: true, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                database.CleanAt = null;
                return Result(MigrationStepOutcome.Error, ex, $"Migrating the test database to the previous migration '{previous}'");
            }
        }

        // Plant and migrate for real; the planted data stays, so the next step rebuilds.
        database.CleanAt = null;
        SeedResult seed;
        try
        {
            seed = await SeedAsync(context, operations, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(MigrationStepOutcome.Error, ex, stage);
        }

        warnings.AddRange(seed.Warnings);
        try
        {
            await context.Database.MigrateAsync(migrationId, cancellationToken).ConfigureAwait(false);
            return Result(MigrationStepOutcome.Passed, scenarios: seed.Scenarios, sql: seed.SqlExecuted);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return Result(MigrationStepOutcome.Failed, ex, scenarios: seed.Scenarios, sql: seed.SqlExecuted);
        }
    }

    /// <summary>
    /// Brings the database to a clean <paramref name="previous"/>: migrating forward from a clean earlier state
    /// when it has one, otherwise from an empty database.
    /// </summary>
    private static async Task PrepareAsync(
        DbContext context,
        TestDatabase database,
        List<string> all,
        string? previous,
        bool rebuild,
        CancellationToken cancellationToken)
    {
        bool canMoveForward = !rebuild
            && previous is not null
            && database.CleanAt is { } at
            && all.IndexOf(at) >= 0
            && all.IndexOf(at) <= all.IndexOf(previous);
        if (!canMoveForward)
        {
            database.CleanAt = null;
            await context.Database.EnsureDeletedAsync(cancellationToken).ConfigureAwait(false);
        }

        if (previous is not null && database.CleanAt != previous)
        {
            await context.Database.MigrateAsync(previous, cancellationToken).ConfigureAwait(false);
        }

        database.CleanAt = previous;
    }

    /// <summary>Applies the migration to the clean database so the next step can start from it.</summary>
    private static async Task AdvanceAsync(
        DbContext context,
        TestDatabase database,
        string migrationId,
        List<string> warnings,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.Database.MigrateAsync(migrationId, cancellationToken).ConfigureAwait(false);
            database.CleanAt = migrationId;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            database.CleanAt = null;
            warnings.Add($"Applying the migration to the database without planted data also failed: {ex.Message}");
        }
    }

    private sealed record TransactionalRun(SeedResult Seed, Exception? Failure);

    /// <summary>
    /// Plants the data and runs the migration's script in one transaction, then rolls it back.
    /// Null when a seed error ended the transaction early.
    /// </summary>
    private static async Task<TransactionalRun?> RunInTransactionAsync(
        DbContext context,
        string previous,
        string migrationId,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken)
    {
        var batches = ScriptBatches(context.GetService<IMigrator>()
            .GenerateScript(previous, migrationId, MigrationsSqlGenerationOptions.NoTransactions));
        int? timeout = context.Database.GetCommandTimeout();

        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = context.Database.GetDbConnection();
            var schema = await LoadSchemaAsync(context, connection, cancellationToken).ConfigureAwait(false);

            // An ADO.NET transaction, not BEGIN TRANSACTION: under MARS a T-SQL transaction ends with its batch.
            var transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                try
                {
                    var seed = await AdversarySeeder.SeedAsync(
                            connection, schema, ScenarioPlanner.Plan(operations, schema), cancellationToken, transaction)
                        .ConfigureAwait(false);
                    if (await TransactionStateAsync(transaction, cancellationToken).ConfigureAwait(false) != 1)
                    {
                        return null;
                    }

                    foreach (string batch in batches)
                    {
                        try
                        {
                            await ExecuteAsync(transaction, batch, timeout, cancellationToken).ConfigureAwait(false);
                        }
                        catch (DbException ex)
                        {
                            return new TransactionalRun(seed, ex);
                        }
                    }

                    return new TransactionalRun(seed, null);
                }
                finally
                {
                    // Null once SQL Server has rolled it back itself.
                    if (transaction.Connection is not null)
                    {
                        await transaction.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
                    }
                }
            }
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static async Task<SeedResult> SeedAsync(
        DbContext context,
        IReadOnlyList<MigrationOperation> operations,
        CancellationToken cancellationToken)
    {
        await context.Database.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var connection = context.Database.GetDbConnection();
            var schema = await LoadSchemaAsync(context, connection, cancellationToken).ConfigureAwait(false);
            return await AdversarySeeder.SeedAsync(connection, schema, ScenarioPlanner.Plan(operations, schema), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            await context.Database.CloseConnectionAsync().ConfigureAwait(false);
        }
    }

    private static Task<RelationalSchemaModel> LoadSchemaAsync(DbContext context, DbConnection connection, CancellationToken cancellationToken)
    {
        var relational = RelationalOptionsExtension.Extract(context.GetService<IDbContextOptions>());
        return RelationalSchemaIntrospector.LoadAsync(
            connection,
            relational.MigrationsHistoryTableSchema,
            relational.MigrationsHistoryTableName ?? HistoryRepository.DefaultTableName,
            cancellationToken);
    }

    /// <summary>Statements EF runs outside any transaction, which a rolled-back run cannot include.</summary>
    private static bool NeedsItsOwnTransaction(IReadOnlyList<MigrationOperation> operations)
        => operations.Any(static o => o is SqlOperation { SuppressTransaction: true } or AlterDatabaseOperation);

    /// <summary>Splits a SQL Server migration script on its <c>GO</c> batch separators.</summary>
    internal static List<string> ScriptBatches(string script)
    {
        var batches = new List<string>();
        var current = new StringBuilder();
        foreach (string rawLine in script.Split('\n'))
        {
            string line = rawLine.TrimEnd('\r');
            if (string.Equals(line.Trim(), "GO", StringComparison.OrdinalIgnoreCase))
            {
                Flush();
            }
            else
            {
                current.AppendLine(line);
            }
        }

        Flush();
        return batches;

        void Flush()
        {
            string batch = current.ToString().Trim();
            if (batch.Length > 0)
            {
                batches.Add(batch);
            }

            current.Clear();
        }
    }

    private static async Task ExecuteAsync(DbTransaction transaction, string sql, int? timeout, CancellationToken cancellationToken)
    {
        await using var cmd = transaction.Connection!.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = sql;
        if (timeout is int seconds)
        {
            cmd.CommandTimeout = seconds;
        }

        await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary><c>XACT_STATE()</c>: 1 for a usable transaction, 0 for none, -1 for one that can only be rolled back.</summary>
    private static async Task<int> TransactionStateAsync(DbTransaction transaction, CancellationToken cancellationToken)
    {
        if (transaction.Connection is not { } connection)
        {
            return 0;
        }

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = "SELECT XACT_STATE();";
        return Convert.ToInt32(await cmd.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false), CultureInfo.InvariantCulture);
    }
}
