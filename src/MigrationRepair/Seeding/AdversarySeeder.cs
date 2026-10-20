using System.Data.Common;
using MigrationRepair.Planning;
using MigrationRepair.Schema;

namespace MigrationRepair.Seeding;

internal sealed class SeedResult
{
    /// <summary>The planned scenarios, in order; those that could not be planted are marked skipped.</summary>
    public required IReadOnlyList<AdversaryScenario> Scenarios { get; init; }

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
    public static async Task<SeedResult> SeedAsync(
        DbConnection connection,
        RelationalSchemaModel schema,
        IReadOnlyList<AdversaryScenario> scenarios,
        CancellationToken cancellationToken)
    {
        var sqlLog = new List<string>();
        var warnings = new List<string>();

        var noBaselineRow = new Dictionary<TableName, string>();
        foreach (var table in TableInsertOrder.Topological(schema.Tables))
        {
            var insert = SeedSqlGenerator.BaselineInsert(table);
            string? failure = insert.Succeeded
                ? await TryExecuteAsync(connection, insert.Sql, sqlLog, cancellationToken).ConfigureAwait(false)
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
            .OrderBy(i => scenarios[i].Kind == AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint ? 1 : 0);
        foreach (int i in order)
        {
            var scenario = scenarios[i];
            if (scenario.Skipped)
            {
                continue;
            }

            string? failure = await PlantAsync(scenario).ConfigureAwait(false);
            if (failure is not null)
            {
                outcome[i] = scenario with { Skipped = true, SkipReason = $"Not seeded: {failure}" };
            }
        }

        return new SeedResult { Scenarios = outcome, SqlExecuted = sqlLog, Warnings = warnings };

        async Task<string?> PlantAsync(AdversaryScenario scenario)
        {
            if (schema.FindTable(scenario.Schema, scenario.Table) is not { } table)
            {
                return "table does not exist at N-1";
            }

            if (noBaselineRow.TryGetValue(table.Name, out string? why))
            {
                return $"the table has no row to plant data in ({why})";
            }

            SeedSql statement;
            IReadOnlyList<ColumnValue> planted = [];
            if (scenario.Kind == AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint)
            {
                if (scenario.NotNullFilterColumns.Count == 0)
                {
                    statement = SeedSqlGenerator.CopyRow(table, scenario.Columns, [], NextOrdinal(table.Name));
                }
                else if (SeedSqlGenerator.TryGetFilterValues(scenario, table, out var filterValues, out string? filterFailure))
                {
                    // Two new rows, both inside the filter; existing rows may hold NULLs other scenarios planted.
                    statement = SeedSqlGenerator.CopyRows(
                        table, scenario.Columns, filterValues, [NextOrdinal(table.Name), NextOrdinal(table.Name)]);
                }
                else
                {
                    return filterFailure;
                }
            }
            else if (!SeedSqlGenerator.TryGetBadValues(scenario, table, out planted, out string? failure))
            {
                return failure;
            }
            else if (planted.Count == 0)
            {
                // The baseline row is all this scenario needs.
                return null;
            }
            else
            {
                // A column that already holds another scenario's bad value gets a row of its own.
                bool columnTaken = planted.Any(v => mutatedColumns.Contains(Key(table.Name, v.Column)));
                statement = columnTaken
                    ? SeedSqlGenerator.CopyRow(table, [], planted, NextOrdinal(table.Name))
                    : SeedSql.Ok(SeedSqlGenerator.UpdateOneRow(table, planted));
            }

            if (!statement.Succeeded)
            {
                return statement.Failure;
            }

            string? executeFailure = await TryExecuteAsync(connection, statement.Sql, sqlLog, cancellationToken)
                .ConfigureAwait(false);
            if (executeFailure is null)
            {
                foreach (var v in planted)
                {
                    mutatedColumns.Add(Key(table.Name, v.Column));
                }
            }

            return executeFailure;
        }

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
        string sql,
        List<string> sqlLog,
        CancellationToken cancellationToken)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        try
        {
            int affected = await cmd.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            sqlLog.Add(sql);

            // -1 means row counts are off (SET NOCOUNT ON); only a reported 0 is a failure.
            return affected == 0 ? "the statement affected no rows (a referenced table may be empty)" : null;
        }
        catch (DbException ex)
        {
            return $"{ex.Message} [{sql}]";
        }
    }
}
