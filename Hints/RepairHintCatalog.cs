using MigrationRepair.Planning;

namespace MigrationRepair.Hints;

/// <summary>
/// Portable SQL repair patterns with optional per-engine notes.
/// </summary>
public static class RepairHintCatalog
{
    public static string ForScenario(AdversaryScenario scenario, DatabaseFamily family = DatabaseFamily.Portable)
    {
        return scenario.Kind switch
        {
            AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint => DuplicateUnique(scenario, family),
            AdversaryScenarioKind.ForeignKeyOrphan => ForeignKeyOrphan(scenario, family),
            AdversaryScenarioKind.NonNullableColumnWithNull => NonNullable(scenario, family),
            AdversaryScenarioKind.MaxLengthExceededAfterShrink => MaxLength(scenario, family),
            AdversaryScenarioKind.CheckConstraintViolation => Check(scenario, family),
            AdversaryScenarioKind.UnsupportedOperation => "Review migration operations manually; add data cleanup or reorder steps.",
            AdversaryScenarioKind.SkippedTableNotPresentAtNMinus1 => scenario.SkipReason ?? "Skipped.",
            _ => "Review migration and pre-migration data."
        };
    }

    private static string DuplicateUnique(AdversaryScenario s, DatabaseFamily family)
    {
        var cols = string.Join(", ", s.Columns.Select(Quote));
        var table = Quote(s.Table);
        var portable = $"""
            -- Deduplicate before creating the unique index/constraint.
            -- Example: keep one row per distinct ({cols}), e.g. lowest primary key:
            -- 1) Identify duplicates (adjust PK column name):
            -- SELECT {cols}, MIN("Id") AS keep_id FROM {table} GROUP BY {cols} HAVING COUNT(*) > 1;
            -- 2) Delete or merge extras:
            -- DELETE FROM {table} t WHERE t."Id" NOT IN (SELECT keep_id FROM ...);
            """;
        return family switch
        {
            DatabaseFamily.MySql => portable + """

                MySQL notes: use DELETE t FROM table t INNER JOIN (...) d ON ...; 
                verify collation on string columns (duplicates depend on collation).
                """,
            DatabaseFamily.SqlServer => portable + """

                SQL Server: consider ROW_NUMBER() OVER (PARTITION BY ... ORDER BY id) AS rn and DELETE WHERE rn > 1.
                """,
            DatabaseFamily.PostgreSql => portable + """

                PostgreSQL: DELETE USING or ctid-based patterns work well for deduplication.
                """,
            _ => portable
        };
    }

    private static string ForeignKeyOrphan(AdversaryScenario s, DatabaseFamily family)
    {
        var dep = Quote(s.Table);
        var principal = s.PrincipalTable is { } p ? Quote(p) : "<principal_table>";
        var childCols = string.Join(", ", s.Columns.Select(Quote));
        var parentCols = string.Join(", ", (s.PrincipalColumns ?? []).Select(Quote));
        var portable = $"""
            -- Remove or fix rows in {dep} whose ({childCols}) do not exist in {principal} ({parentCols}).
            -- Pattern: DELETE FROM {dep} c WHERE NOT EXISTS (
            --   SELECT 1 FROM {principal} p WHERE <match child FK columns to parent PK columns>
            -- );
            """;
        return family switch
            {
                DatabaseFamily.MySql => portable + "\nMySQL: prefer targeted DELETE/UPDATE over disabling FOREIGN_KEY_CHECKS.",
                _ => portable
            };
    }

    private static string NonNullable(AdversaryScenario s, DatabaseFamily family)
    {
        var col = Quote(s.Columns[0]);
        var table = Quote(s.Table);
        return $"""
            -- Column {col} becomes NOT NULL: set a default for existing NULLs before the ALTER.
            UPDATE {table} SET {col} = <sentinel_or_derived_value> WHERE {col} IS NULL;
            """;
    }

    private static string MaxLength(AdversaryScenario s, DatabaseFamily family)
    {
        var col = Quote(s.Columns[0]);
        var table = Quote(s.Table);
        var n = s.NewMaxLength?.ToString() ?? "?";
        return family switch
        {
            DatabaseFamily.MySql => $"""
                -- Shorten values before shrinking the column (strict mode errors instead of silent truncate):
                UPDATE {table} SET {col} = LEFT({col}, {n}) WHERE CHAR_LENGTH({col}) > {n};
                """,
            DatabaseFamily.SqlServer => $"""
                UPDATE {table} SET {col} = LEFT({col}, {n}) WHERE LEN({col}) > {n};
                """,
            DatabaseFamily.PostgreSql => $"""
                UPDATE {table} SET {col} = LEFT({col}::text, {n})::varchar WHERE length({col}::text) > {n};
                """,
            _ => $"""
                -- Truncate or split overflow data before ALTER:
                UPDATE {table} SET {col} = SUBSTR({col}, 1, {n}) WHERE length({col}) > {n};
                """
        };
    }

    private static string Check(AdversaryScenario s, DatabaseFamily family)
    {
        return """
            -- CHECK constraint: update or delete rows that fail the new predicate.
            -- Inspect the constraint SQL in the migration, then run a targeted UPDATE/DELETE
            -- (often with a SELECT ... WHERE NOT (<predicate>) dry-run first).
            """;
    }

    private static string Quote(string identifier)
        => "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
