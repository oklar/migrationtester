using System.Data.Common;
using MigrationTester.Planning;
using MigrationTester.Schema;

namespace MigrationTester.Seeding;

internal sealed class SeedResult
{
    /// <summary>The planned scenarios, in order; those that could not be planted are marked <see cref="ScenarioStatus.NotSeeded"/>.</summary>
    public required IReadOnlyList<MigrationScenario> Scenarios { get; init; }

    /// <summary>Statements that ran successfully, in order.</summary>
    public required IReadOnlyList<string> SqlExecuted { get; init; }

    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>
/// Runs the seed SQL against the N-1 database. A statement that fails marks its table or scenario as not
/// seeded and seeding carries on, so one unsupported table never aborts the run.
/// </summary>
internal static class AdversarySeeder
{
    // A row copy may need new rows in a principal table, whose copies may need new rows in its principal, and so on.
    private const int MaxPrincipalDepth = 3;

    public static async Task<SeedResult> SeedAsync(
        DbConnection connection,
        RelationalSchemaModel schema,
        IReadOnlyList<MigrationScenario> scenarios,
        CancellationToken cancellationToken,
        DbTransaction? transaction = null)
    {
        var sqlLog = new List<string>();
        var warnings = new List<string>();

        var noBaselineRow = new Dictionary<TableName, string>();
        foreach (var table in TableInsertOrder.Topological(schema.Tables))
        {
            if (SeedSqlGenerator.ReseedIdentity(table) is { } reseed)
            {
                // Best effort: without it the seeded ids are merely more likely to meet HasData ids.
                await TryExecuteAsync(connection, transaction, reseed, sqlLog, cancellationToken, expectRows: false).ConfigureAwait(false);
            }

            var insert = SeedSqlGenerator.BaselineInsert(table);
            string? failure = insert.Succeeded
                ? await TryExecuteAsync(connection, transaction, insert.Sql, sqlLog, cancellationToken).ConfigureAwait(false)
                : insert.Failure;
            if (failure is not null)
            {
                noBaselineRow[table.Name] = failure;
                warnings.Add($"No baseline row in {table.Name}: {failure}");
            }
        }

        var outcome = scenarios.ToArray();
        var mutatedColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var nextCopyOrdinal = new Dictionary<TableName, int>();

        // Single-row mutations first, so the duplicates copied afterwards keep identical keys.
        var order = Enumerable.Range(0, scenarios.Count)
            .OrderBy(i => scenarios[i].Kind == ScenarioKind.DuplicateUniqueIndexOrConstraint ? 1 : 0);
        foreach (int i in order)
        {
            var scenario = scenarios[i];
            if (scenario.Status != ScenarioStatus.Planted)
            {
                continue;
            }

            string? failure = await PlantAsync(scenario).ConfigureAwait(false);
            if (failure is not null)
            {
                outcome[i] = scenario.NotSeeded(failure);
            }
        }

        return new SeedResult { Scenarios = outcome, SqlExecuted = sqlLog, Warnings = warnings };

        async Task<string?> PlantAsync(MigrationScenario scenario)
        {
            if (schema.FindTable(scenario.Schema, scenario.Table) is not { } table)
            {
                return "table does not exist at N-1";
            }

            if (noBaselineRow.TryGetValue(table.Name, out string? why))
            {
                return $"the table has no row to plant data in ({why})";
            }

            if (scenario.Kind == ScenarioKind.DuplicateUniqueIndexOrConstraint)
            {
                if (scenario.NotNullFilterColumns.Count == 0)
                {
                    return await CopyAsync(table, scenario.Columns, [], copies: 1).ConfigureAwait(false);
                }

                // Two new rows, both inside the filter; existing rows may hold NULLs other scenarios planted.
                return SeedSqlGenerator.TryGetFilterValues(scenario, table, out var filterValues, out string? filterFailure)
                    ? await CopyAsync(table, scenario.Columns, filterValues, copies: 2).ConfigureAwait(false)
                    : filterFailure;
            }

            if (!SeedSqlGenerator.TryGetBadValues(scenario, table, out var planted, out string? failure))
            {
                return failure;
            }

            if (planted.Count == 0)
            {
                // The baseline row is all this scenario needs.
                return null;
            }

            if (SeedSqlGenerator.ExistingForeignKey(scenario, table) is { } existing)
            {
                // The orphan is a key of the existing foreign key's principal that the new principal lacks, and the
                // first rows of both tables often have the same key: add a principal row with a key of its own.
                string? principalFailure = schema.FindTable(existing.PrincipalTable) is { } oldPrincipal
                    ? await CopyAsync(oldPrincipal, [], [], copies: 1).ConfigureAwait(false)
                    : "the table does not exist at N-1";
                if (principalFailure is not null)
                {
                    return $"no row could be added to {existing.PrincipalTable}, which '{existing.Name}' references: {principalFailure}";
                }
            }

            // A column that already holds another scenario's bad value gets a row of its own.
            bool columnTaken = planted.Any(v => mutatedColumns.Contains(Key(table.Name, v.Column)));
            string? plantFailure = columnTaken
                ? await CopyAsync(table, [], planted, copies: 1).ConfigureAwait(false)
                : await ExecuteAsync(SeedSqlGenerator.UpdateOneRow(table, planted)).ConfigureAwait(false);
            if (plantFailure is null)
            {
                foreach (var v in planted)
                {
                    mutatedColumns.Add(Key(table.Name, v.Column));
                }
            }

            return plantFailure;
        }

        // Copies a row, after adding the principal rows whose keys the copies take for a unique key of foreign key columns.
        async Task<string?> CopyAsync(
            TableMetadata table,
            IReadOnlyCollection<string> keep,
            IReadOnlyList<ColumnValue> overrides,
            int copies,
            int depth = 0)
        {
            foreach (var principalName in SeedSqlGenerator.PrincipalsForCopies(table, keep, overrides))
            {
                string? failure = depth >= MaxPrincipalDepth ? $"principal rows are needed more than {MaxPrincipalDepth} tables away"
                    : schema.FindTable(principalName) is { } principal ? await CopyAsync(principal, [], [], copies, depth + 1).ConfigureAwait(false)
                    : "the table does not exist at N-1";
                if (failure is not null)
                {
                    return $"no row could be added to {principalName} for a copied row to reference: {failure}";
                }
            }

            var statement = SeedSqlGenerator.CopyRows(
                table, keep, overrides, [.. Enumerable.Range(0, copies).Select(_ => NextOrdinal(table.Name))]);
            return statement.Succeeded ? await ExecuteAsync(statement.Sql).ConfigureAwait(false) : statement.Failure;
        }

        Task<string?> ExecuteAsync(string sql) => TryExecuteAsync(connection, transaction, sql, sqlLog, cancellationToken);

        int NextOrdinal(TableName table)
        {
            int ordinal = nextCopyOrdinal.GetValueOrDefault(table, 2);
            nextCopyOrdinal[table] = ordinal + 1;
            return ordinal;
        }
    }

    private static string Key(TableName table, string column) => $"{table}|{column}";

    /// <summary>Runs one statement; returns null on success, otherwise why it did not plant a row.</summary>
    private static async Task<string?> TryExecuteAsync(
        DbConnection connection,
        DbTransaction? transaction,
        string sql,
        List<string> sqlLog,
        CancellationToken cancellationToken,
        bool expectRows = true)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        cmd.Transaction = transaction;
        try
        {
            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            sqlLog.Add(sql);

            // -1 means row counts are off (SET NOCOUNT ON); only a reported 0 is a failure.
            return expectRows && affected == 0 ? "the statement affected no rows (a referenced table may be empty)" : null;
        }
        catch (DbException ex)
        {
            return $"{ex.Message} [{sql}]";
        }
        catch (InvalidOperationException ex) when (transaction is not null && transaction.Connection is null)
        {
            // An earlier error made SQL Server roll the transaction back; the caller checks for this afterwards.
            return ex.Message;
        }
    }
}
