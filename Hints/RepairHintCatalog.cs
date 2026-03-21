using MigrationRepair.Planning;
using MigrationRepair.Seeding;

namespace MigrationRepair.Hints;

/// <summary>
/// SQL Server–oriented repair patterns for failed migrations.
/// </summary>
public static class RepairHintCatalog
{
    public static string ForScenario(AdversaryScenario scenario)
    {
        return scenario.Kind switch
        {
            AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint => DuplicateUnique(scenario),
            AdversaryScenarioKind.ForeignKeyOrphan => ForeignKeyOrphan(scenario),
            AdversaryScenarioKind.NonNullableColumnWithNull => NonNullable(scenario),
            AdversaryScenarioKind.MaxLengthExceededAfterShrink => MaxLength(scenario),
            AdversaryScenarioKind.CheckConstraintViolation => Check(scenario),
            AdversaryScenarioKind.UnsupportedOperation => "Review migration operations manually; add data cleanup or reorder steps.",
            AdversaryScenarioKind.SkippedTableNotPresentAtNMinus1 => scenario.SkipReason ?? "Skipped.",
            _ => "Review migration and pre-migration data."
        };
    }

    private static string DuplicateUnique(AdversaryScenario s)
    {
        string cols = string.Join(", ", s.Columns.Select(c => SqlDialect.Q(c)));
        string table = SqlDialect.Q(s.Table);
        return $"""
            -- Deduplicate before creating the unique index/constraint.
            -- Example: keep one row per distinct ({cols}), e.g. lowest primary key:
            -- 1) Identify duplicates (adjust PK column name):
            -- SELECT {cols}, MIN([Id]) AS keep_id FROM {table} GROUP BY {cols} HAVING COUNT(*) > 1;
            -- 2) Delete extras, e.g. with ROW_NUMBER() OVER (PARTITION BY ... ORDER BY [Id]) AS rn then DELETE WHERE rn > 1.
            """;
    }

    private static string ForeignKeyOrphan(AdversaryScenario s)
    {
        string dep = SqlDialect.Q(s.Table);
        string principal = s.PrincipalTable is { } p ? SqlDialect.Q(p) : "[PrincipalTable]";
        string childCols = string.Join(", ", s.Columns.Select(SqlDialect.Q));
        string parentCols = string.Join(", ", (s.PrincipalColumns ?? []).Select(SqlDialect.Q));
        return $"""
            -- Remove or fix rows in {dep} whose ({childCols}) do not exist in {principal} ({parentCols}).
            -- Pattern: DELETE FROM {dep} c WHERE NOT EXISTS (
            --   SELECT 1 FROM {principal} p WHERE <match child FK columns to parent PK columns>
            -- );
            """;
    }

    private static string NonNullable(AdversaryScenario s)
    {
        string col = SqlDialect.Q(s.Columns[0]);
        string table = SqlDialect.Q(s.Table);
        return $"""
            -- Column {col} becomes NOT NULL: set a default for existing NULLs before the ALTER.
            UPDATE {table} SET {col} = <sentinel_or_derived_value> WHERE {col} IS NULL;
            """;
    }

    private static string MaxLength(AdversaryScenario s)
    {
        string col = SqlDialect.Q(s.Columns[0]);
        string table = SqlDialect.Q(s.Table);
        string n = s.NewMaxLength?.ToString() ?? "?";
        return $"""
            UPDATE {table} SET {col} = LEFT({col}, {n}) WHERE LEN({col}) > {n};
            """;
    }

    private static string Check(AdversaryScenario s)
    {
        return """
            -- CHECK constraint: update or delete rows that fail the new predicate.
            -- Inspect the constraint SQL in the migration, then run a targeted UPDATE/DELETE
            -- (often with a SELECT ... WHERE NOT (<predicate>) dry-run first).
            """;
    }
}
