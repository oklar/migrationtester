using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Seeding;

namespace MigrationTester.Planning;

/// <summary>
/// SQL Server-oriented repair patterns for failed migrations.
/// </summary>
internal static class RepairHintCatalog
{
    public static string ForScenario(MigrationScenario scenario)
    {
        return scenario.Kind switch
        {
            ScenarioKind.DuplicateUniqueIndexOrConstraint => DuplicateUnique(scenario),
            ScenarioKind.ForeignKeyOrphan => ForeignKeyOrphan(scenario),
            ScenarioKind.NonNullableColumnWithNull => NonNullable(scenario),
            ScenarioKind.MaxLengthExceededAfterShrink => MaxLength(scenario),
            ScenarioKind.CheckConstraintViolation => Check(scenario),
            ScenarioKind.NotNullColumnAddedWithoutDefault => NotNullAddColumn(scenario),
            ScenarioKind.ColumnTypeConversionIncompatible => TypeConversion(scenario),
            ScenarioKind.DecimalPrecisionOverflow => PrecisionOverflow(scenario),
            _ => "Review migration and pre-migration data."
        };
    }

    private static string DuplicateUnique(MigrationScenario s)
    {
        string cols = string.Join(", ", s.Columns.Select(c => SqlDialect.Q(c)));
        string table = SqlDialect.Q(s.Schema, s.Table);
        return $"""
            -- Deduplicate before creating the unique index/constraint.
            -- Example: keep one row per distinct ({cols}), e.g. lowest primary key:
            -- 1) Identify duplicates (adjust PK column name):
            -- SELECT {cols}, MIN([Id]) AS keep_id FROM {table} GROUP BY {cols} HAVING COUNT(*) > 1;
            -- 2) Delete extras, e.g. with ROW_NUMBER() OVER (PARTITION BY ... ORDER BY [Id]) AS rn then DELETE WHERE rn > 1.
            """;
    }

    private static string ForeignKeyOrphan(MigrationScenario s)
    {
        string dep = SqlDialect.Q(s.Schema, s.Table);
        string principal = s.PrincipalTable is { } p ? SqlDialect.Q(s.PrincipalSchema, p) : "[PrincipalTable]";
        string childCols = string.Join(", ", s.Columns.Select(SqlDialect.Q));
        string parentCols = string.Join(", ", (s.PrincipalColumns ?? []).Select(SqlDialect.Q));
        return $"""
            -- Remove or fix rows in {dep} whose ({childCols}) do not exist in {principal} ({parentCols}).
            -- Pattern: DELETE FROM {dep} c WHERE NOT EXISTS (
            --   SELECT 1 FROM {principal} p WHERE <match child FK columns to parent PK columns>
            -- );
            """;
    }

    private static string NonNullable(MigrationScenario s)
    {
        string col = SqlDialect.Q(s.Columns[0]);
        string table = SqlDialect.Q(s.Schema, s.Table);
        return $"""
            -- Column {col} becomes NOT NULL: set a default for existing NULLs before the ALTER.
            UPDATE {table} SET {col} = <sentinel_or_derived_value> WHERE {col} IS NULL;
            """;
    }

    private static string MaxLength(MigrationScenario s)
    {
        string col = SqlDialect.Q(s.Columns[0]);
        string table = SqlDialect.Q(s.Schema, s.Table);
        string n = s.NewMaxLength?.ToString() ?? "?";
        return $"""
            UPDATE {table} SET {col} = LEFT({col}, {n}) WHERE LEN({col}) > {n};
            """;
    }

    private static string Check(MigrationScenario s)
    {
        if (s.SourceOperation is AddCheckConstraintOperation { Sql: { Length: > 0 } predicate } chk)
        {
            string table = SqlDialect.Q(s.Schema, s.Table);
            return $"""
                -- CHECK constraint [{chk.Name}]: update or delete the rows that fail ({predicate}) before adding it.
                -- Find them: SELECT * FROM {table} WHERE NOT ({predicate});
                """;
        }

        return """
            -- CHECK constraint: update or delete rows that fail the new predicate.
            -- Inspect the constraint SQL in the migration, then run a targeted UPDATE/DELETE
            -- (often with a SELECT ... WHERE NOT (<predicate>) dry-run first).
            """;
    }

    private static string NotNullAddColumn(MigrationScenario s)
    {
        string col = SqlDialect.Q(s.Columns[0]);
        string table = SqlDialect.Q(s.Schema, s.Table);
        return $"""
            -- Column {col} is added as NOT NULL without a default value.
            -- Existing rows cannot satisfy the constraint. Either:
            -- 1) Add a DEFAULT in the migration: .HasDefaultValue(...) / .HasDefaultValueSql(...)
            -- 2) Split into two steps: add as nullable, UPDATE with a value, then ALTER to NOT NULL.
            ALTER TABLE {table} ADD {col} <type> NOT NULL CONSTRAINT [DF_{s.Table}_{s.Columns[0]}] DEFAULT <value>;
            """;
    }

    private static string TypeConversion(MigrationScenario s)
    {
        string col = SqlDialect.Q(s.Columns[0]);
        string table = SqlDialect.Q(s.Schema, s.Table);
        string oldT = s.OldColumnType ?? "?";
        string newT = s.NewColumnType ?? "?";
        return $"""
            -- Column {col} changes from {oldT} to {newT}.
            -- Existing data may not be convertible. Before altering:
            -- 1) Find unconvertible rows: SELECT * FROM {table} WHERE TRY_CAST({col} AS {newT}) IS NULL AND {col} IS NOT NULL;
            -- 2) Fix or remove them: UPDATE {table} SET {col} = <safe_value> WHERE TRY_CAST({col} AS {newT}) IS NULL;
            """;
    }

    private static string PrecisionOverflow(MigrationScenario s)
    {
        string col = SqlDialect.Q(s.Columns[0]);
        string table = SqlDialect.Q(s.Schema, s.Table);
        int newP = s.NewPrecision ?? 0;
        int newS = s.NewScale ?? 0;
        int intDigits = newP - newS;
        return $"""
            -- Column {col} precision shrinks to ({newP},{newS}): max {intDigits} integer digits and {newS} decimals.
            -- Find overflow rows: SELECT * FROM {table} WHERE {col} >= POWER(10, {intDigits}) OR {col} <= -POWER(10, {intDigits});
            -- Round excess decimals: UPDATE {table} SET {col} = ROUND({col}, {newS}) WHERE {col} IS NOT NULL;
            """;
    }
}
