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
        bool constraintConflict = false;
        foreach (SqlError error in sql.Errors)
        {
            kinds.UnionWith(KindsFor(error.Number));
            constraintConflict |= error.Number == 547;
        }

        string message = string.Join(" ", sql.Errors.Cast<SqlError>().Select(static e => e.Message));
        var candidates = scenarios.Where(s => s.Status == ScenarioStatus.Planted && kinds.Contains(s.Kind)).ToList();
        if (constraintConflict)
        {
            // 547 names the FOREIGN KEY or CHECK constraint that rejected the data; data planted for other constraints did not break it.
            candidates.RemoveAll(s =>
                (s.Kind is ScenarioKind.ForeignKeyOrphan or ScenarioKind.CheckConstraintViolation) && !IsObjectNamedIn(message, s));
        }

        if (candidates.Count <= 1)
        {
            return candidates;
        }

        // Several of the right kind: keep those whose constraint, index or column the message names.
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
        => IsObjectNamedIn(message, scenario)
            || scenario.Columns.Concat(ColumnsInOperation(scenario)).Any(c => message.Contains($"'{c}'", StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Columns as the failing statement names them. Scenarios hold N-1 column names, which differ after a rename
    /// earlier in the same migration.
    /// </summary>
    private static string[] ColumnsInOperation(MigrationScenario scenario) => scenario.SourceOperation switch
    {
        CreateIndexOperation i => i.Columns,
        AddForeignKeyOperation f => f.Columns,
        AddUniqueConstraintOperation u => u.Columns,
        AddPrimaryKeyOperation p => p.Columns,
        AlterColumnOperation a => [a.Name],
        AddColumnOperation c => [c.Name],
        _ => []
    };

    /// <summary>True when the message names the index or constraint the scenario's operation creates.</summary>
    private static bool IsObjectNamedIn(string message, MigrationScenario scenario)
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

        return !string.IsNullOrEmpty(objectName) && message.Contains(objectName, StringComparison.OrdinalIgnoreCase);
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
