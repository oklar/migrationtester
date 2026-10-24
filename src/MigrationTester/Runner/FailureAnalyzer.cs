using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MigrationTester.Runner;

/// <summary>Matches a migration's SQL Server error to the planted scenarios that caused it.</summary>
internal static class FailureAnalyzer
{
    public static IReadOnlyList<MigrationScenario> FailingScenarios(Exception exception, IReadOnlyList<MigrationScenario> scenarios)
    {
        if (FindSqlException(exception) is not { } sql)
        {
            return [];
        }

        var kinds = new HashSet<ScenarioKind>();
        foreach (SqlError error in sql.Errors)
        {
            kinds.UnionWith(KindsFor(error.Number));
        }

        var candidates = scenarios.Where(s => s.Status == ScenarioStatus.Planted && kinds.Contains(s.Kind)).ToList();
        if (candidates.Count <= 1)
        {
            return candidates;
        }

        // Several of the right kind: keep those whose constraint, index or column the message names.
        string message = string.Join(" ", sql.Errors.Cast<SqlError>().Select(static e => e.Message));
        var named = candidates.Where(s => IsNamedIn(message, s)).ToList();
        return named.Count > 0 ? named : candidates;
    }

    /// <summary>Scenario kinds a SQL Server error number points to.</summary>
    internal static IEnumerable<ScenarioKind> KindsFor(int errorNumber) => errorNumber switch
    {
        // CREATE UNIQUE INDEX / ADD CONSTRAINT found a duplicate key; 2601/2627 are the row-level versions.
        1505 or 1750 or 2601 or 2627 => [ScenarioKind.DuplicateUniqueIndexOrConstraint],
        // ALTER TABLE conflicted with a FOREIGN KEY or CHECK constraint.
        547 => [ScenarioKind.ForeignKeyOrphan, ScenarioKind.CheckConstraintViolation],
        // Cannot insert the value NULL into column ...
        515 => [ScenarioKind.NonNullableColumnWithNull],
        // ALTER TABLE only allows columns to be added that can contain nulls, or have a DEFAULT ...
        4901 => [ScenarioKind.NotNullColumnAddedWithoutDefault],
        // String or binary data would be truncated.
        2628 or 8152 => [ScenarioKind.MaxLengthExceededAfterShrink],
        // Conversion failed / error converting / out-of-range date / XML parsing.
        241 or 242 or 245 or 8114 or 8169 or (>= 9400 and <= 9499) => [ScenarioKind.ColumnTypeConversionIncompatible],
        // Arithmetic overflow.
        220 or 232 or 8115 => [ScenarioKind.DecimalPrecisionOverflow, ScenarioKind.ColumnTypeConversionIncompatible],
        _ => []
    };

    private static bool IsNamedIn(string message, MigrationScenario scenario)
    {
        string? objectName = scenario.SourceOperation switch
        {
            CreateIndexOperation i => i.Name,
            AddForeignKeyOperation f => f.Name,
            AddUniqueConstraintOperation u => u.Name,
            AddPrimaryKeyOperation p => p.Name,
            AddCheckConstraintOperation c => c.Name,
            _ => null
        };

        // Scenarios hold N-1 column names; the message uses the names the failing statement uses, which differ
        // after a rename earlier in the same migration.
        string[] columnsInOperation = scenario.SourceOperation switch
        {
            CreateIndexOperation i => i.Columns,
            AddForeignKeyOperation f => f.Columns,
            AddUniqueConstraintOperation u => u.Columns,
            AddPrimaryKeyOperation p => p.Columns,
            AlterColumnOperation a => [a.Name],
            AddColumnOperation c => [c.Name],
            _ => []
        };

        return (!string.IsNullOrEmpty(objectName) && message.Contains(objectName, StringComparison.OrdinalIgnoreCase))
            || scenario.Columns.Concat(columnsInOperation).Any(c => message.Contains($"'{c}'", StringComparison.OrdinalIgnoreCase));
    }

    private static SqlException? FindSqlException(Exception exception)
    {
        for (var e = exception; e is not null; e = e.InnerException)
        {
            if (e is SqlException sql)
            {
                return sql;
            }
        }

        return null;
    }
}
