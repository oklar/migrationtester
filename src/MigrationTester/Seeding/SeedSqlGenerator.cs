using System.Diagnostics.CodeAnalysis;
using MigrationTester.Planning;
using MigrationTester.Schema;

namespace MigrationTester.Seeding;

/// <summary>A generated statement, or the reason none could be generated.</summary>
internal readonly record struct SeedSql(string? Sql, string? Failure)
{
    [MemberNotNullWhen(true, nameof(Sql))]
    [MemberNotNullWhen(false, nameof(Failure))]
    public bool Succeeded => Sql is not null;

    public static SeedSql Ok(string sql) => new(sql, null);

    public static SeedSql Fail(string reason) => new(null, reason);
}

internal readonly record struct ColumnValue(string Column, string Value);

/// <summary>
/// Seed SQL that stays valid under the N-1 schema: one ordinary row per table whose required foreign
/// keys copy a principal row's key, single-row UPDATEs that plant a bad value, and row copies that
/// keep chosen columns identical while giving every existing unique key a new value.
/// </summary>
internal static class SeedSqlGenerator
{
    public static SeedSql BaselineInsert(TableMetadata table)
    {
        var fromPrincipal = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var sources = new List<string>();
        foreach (var fk in table.ForeignKeys)
        {
            // A foreign key whose columns are all nullable is seeded as NULL.
            if (!fk.Columns.Any(c => table.FindColumn(c) is { IsNullable: false }))
            {
                continue;
            }

            if (fk.PrincipalTable.Equals(table.Name))
            {
                return SeedSql.Fail($"the first row cannot satisfy the required self-referencing foreign key '{fk.Name}'");
            }

            string alias = SqlDialect.Q($"p{sources.Count}");
            sources.Add($"{SqlDialect.Q(fk.PrincipalTable)} AS {alias}");
            for (int i = 0; i < fk.Columns.Count; i++)
            {
                fromPrincipal.TryAdd(fk.Columns[i], $"{alias}.{SqlDialect.Q(fk.PrincipalColumns[i])}");
            }
        }

        var keyColumns = KeyColumns(table);
        var columns = new List<string>();
        var values = new List<string>();
        foreach (var c in table.Columns.Where(static c => c.IsInsertable))
        {
            string? value = fromPrincipal.GetValueOrDefault(c.Name)
                ?? (c.IsNullable ? "NULL" : null)
                ?? (c.HasDefault
                    ? ""
                    : keyColumns.Contains(c.Name) ? SqlLiterals.KeyValue(c.Type, ordinal: 1) : SqlLiterals.Value(c.Type, ordinal: 1));
            if (value is null)
            {
                return SeedSql.Fail($"no value can be generated for column '{c.Name}' of type {c.Type}");
            }

            // Empty means: leave the column to its DEFAULT.
            if (value.Length > 0)
            {
                columns.Add(c.Name);
                values.Add(value);
            }
        }

        string target = SqlDialect.Q(table.Name);
        if (columns.Count == 0)
        {
            return SeedSql.Ok($"INSERT INTO {target} DEFAULT VALUES;");
        }

        string columnList = string.Join(", ", columns.Select(SqlDialect.Q));
        return sources.Count == 0
            ? SeedSql.Ok($"INSERT INTO {target} ({columnList}) VALUES ({string.Join(", ", values)});")
            : SeedSql.Ok(
                $"INSERT INTO {target} ({columnList}) SELECT TOP (1) {string.Join(", ", values)} FROM {string.Join(" CROSS JOIN ", sources)};");
    }

    /// <summary>
    /// Moves the table's identity counter up to <see cref="SqlLiterals.KeyOffset"/> (if it is lower), so seeded
    /// rows get ids that HasData rows, existing or added by a later migration, do not use. Null without identity.
    /// </summary>
    public static string? ReseedIdentity(TableMetadata table)
    {
        if (table.Columns.FirstOrDefault(static c => c.IsIdentity) is not { } identity
            || SqlLiterals.KeyOffset(identity.Type) is not long offset)
        {
            return null;
        }

        string name = SqlLiterals.NString(SqlDialect.Q(table.Name));
        return $"IF IDENT_CURRENT({name}) < {offset} DBCC CHECKIDENT ({name}, RESEED, {offset}) WITH NO_INFOMSGS;";
    }

    public static string UpdateOneRow(TableMetadata table, IReadOnlyList<ColumnValue> assignments)
        => $"UPDATE TOP (1) {SqlDialect.Q(table.Name)} SET {string.Join(", ", assignments.Select(static a => $"{SqlDialect.Q(a.Column)} = {a.Value}"))};";

    /// <summary>
    /// Copies one existing row. <paramref name="keep"/> columns stay identical, <paramref name="overrides"/>
    /// are set, and each existing unique key gets a fresh value in one of its other columns.
    /// </summary>
    public static SeedSql CopyRow(
        TableMetadata table,
        IReadOnlyCollection<string> keep,
        IReadOnlyList<ColumnValue> overrides,
        int ordinal)
        => CopyRows(table, keep, overrides, [ordinal]);

    /// <summary>
    /// Inserts one copy of the same source row per ordinal, as <see cref="CopyRow"/> does. With several
    /// ordinals the copies share their <paramref name="keep"/> and <paramref name="overrides"/> values, so
    /// they duplicate each other even if a different source row would have been picked.
    /// </summary>
    public static SeedSql CopyRows(
        TableMetadata table,
        IReadOnlyCollection<string> keep,
        IReadOnlyList<ColumnValue> overrides,
        IReadOnlyList<int> ordinals)
    {
        var keepSet = new HashSet<string>(keep, StringComparer.OrdinalIgnoreCase);
        var overrideMap = overrides.ToDictionary(static o => o.Column, static o => o.Value, StringComparer.OrdinalIgnoreCase);
        var fkColumns = new HashSet<string>(table.ForeignKeys.SelectMany(static f => f.Columns), StringComparer.OrdinalIgnoreCase);

        // Column -> one fresh value per ordinal.
        var fresh = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);

        foreach (var key in table.UniqueKeys)
        {
            var keyColumns = key.Columns.Select(table.FindColumn).OfType<ColumnMetadata>().ToList();
            if (keyColumns.Any(c => c.IsIdentity || fresh.ContainsKey(c.Name)))
            {
                continue;
            }

            var candidate = keyColumns.FirstOrDefault(c =>
                c.IsInsertable && !keepSet.Contains(c.Name) && !fkColumns.Contains(c.Name) && !overrideMap.ContainsKey(c.Name));
            if (candidate is null)
            {
                return SeedSql.Fail(
                    $"a copied row cannot get a new value for unique key '{key.Name}' without changing the kept or foreign key columns");
            }

            var values = new string[ordinals.Count];
            for (int i = 0; i < ordinals.Count; i++)
            {
                if (SqlLiterals.KeyValue(candidate.Type, ordinals[i]) is not { } value)
                {
                    return SeedSql.Fail($"no value can be generated for column '{candidate.Name}' of type {candidate.Type}");
                }

                values[i] = value;
            }

            fresh[candidate.Name] = values;
        }

        string target = SqlDialect.Q(table.Name);
        var insertable = table.Columns.Where(static c => c.IsInsertable).ToList();
        if (insertable.Count == 0)
        {
            return SeedSql.Ok(string.Join(" ", ordinals.Select(_ => $"INSERT INTO {target} DEFAULT VALUES;")));
        }

        string columnList = string.Join(", ", insertable.Select(static c => SqlDialect.Q(c.Name)));
        if (ordinals.Count == 1)
        {
            var single = insertable.Select(c =>
                overrideMap.GetValueOrDefault(c.Name) ?? fresh.GetValueOrDefault(c.Name)?[0] ?? SqlDialect.Q(c.Name));
            return SeedSql.Ok($"INSERT INTO {target} ({columnList}) SELECT TOP (1) {string.Join(", ", single)} FROM {target};");
        }

        // One scan of one source row, multiplied by a row number [n].[k] that picks each copy's fresh values.
        var multi = insertable.Select(c =>
            overrideMap.GetValueOrDefault(c.Name)
            ?? (fresh.TryGetValue(c.Name, out var perCopy)
                ? $"CASE [n].[k] {string.Join(" ", perCopy.Select((v, i) => $"WHEN {i + 1} THEN {v}"))} END"
                : $"[s].{SqlDialect.Q(c.Name)}"));
        string rowNumbers = string.Join(", ", ordinals.Select((_, i) => $"({i + 1})"));
        return SeedSql.Ok(
            $"INSERT INTO {target} ({columnList}) SELECT {string.Join(", ", multi)} FROM (SELECT TOP (1) * FROM {target}) AS [s] CROSS JOIN (VALUES {rowNumbers}) AS [n]([k]);");
    }

    /// <summary>
    /// Non-NULL values for the columns a filtered unique index requires to be non-NULL: a principal row's key
    /// for foreign key columns, otherwise an ordinary value.
    /// </summary>
    public static bool TryGetFilterValues(
        MigrationScenario scenario,
        TableMetadata table,
        out IReadOnlyList<ColumnValue> values,
        [NotNullWhen(false)] out string? failure)
    {
        var result = new List<ColumnValue>();
        values = result;
        failure = null;
        foreach (string name in scenario.NotNullFilterColumns)
        {
            if (table.FindColumn(name) is not { } column)
            {
                failure = $"filter column '{name}' does not exist at N-1";
                return false;
            }

            var fk = table.ForeignKeys.FirstOrDefault(f => f.Columns.Contains(column.Name, StringComparer.OrdinalIgnoreCase));
            string? value = fk is null
                ? SqlLiterals.KeyValue(column.Type, ordinal: 1)
                : $"(SELECT TOP (1) {SqlDialect.Q(fk.PrincipalColumns[IndexOf(fk.Columns, column.Name)])} FROM {SqlDialect.Q(fk.PrincipalTable)})";
            if (value is null)
            {
                failure = $"no value can be generated for column '{column.Name}' of type {column.Type}";
                return false;
            }

            result.Add(new ColumnValue(column.Name, value));
        }

        return true;
    }

    private static HashSet<string> KeyColumns(TableMetadata table)
        => new(table.UniqueKeys.SelectMany(static k => k.Columns), StringComparer.OrdinalIgnoreCase);

    private static int IndexOf(IReadOnlyList<string> names, string name)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (string.Equals(names[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    /// <summary>
    /// The column values that make <paramref name="scenario"/>'s migration fail. Empty when the scenario
    /// only needs rows to exist (NOT NULL column without default) or is planted by copying a row (duplicates).
    /// </summary>
    public static bool TryGetBadValues(
        MigrationScenario scenario,
        TableMetadata table,
        out IReadOnlyList<ColumnValue> values,
        [NotNullWhen(false)] out string? failure)
    {
        values = [];
        failure = null;
        switch (scenario.Kind)
        {
            case ScenarioKind.NonNullableColumnWithNull:
                if (FindColumn(scenario, table, out var nullable, out failure))
                {
                    if (!nullable.IsNullable)
                    {
                        failure = $"column '{nullable.Name}' is already NOT NULL at N-1";
                        return false;
                    }

                    values = [new ColumnValue(nullable.Name, "NULL")];
                    return true;
                }

                return false;

            case ScenarioKind.MaxLengthExceededAfterShrink:
                if (!FindColumn(scenario, table, out var sized, out failure))
                {
                    return false;
                }

                int length = (scenario.NewMaxLength ?? 0) + 1;
                if (sized.Type.Length is int oldLength && oldLength != SqlType.Max && oldLength < length)
                {
                    failure = $"column '{sized.Name}' ({sized.Type}) cannot hold more than {scenario.NewMaxLength} at N-1";
                    return false;
                }

                if (SqlLiterals.LongValue(sized.Type, length) is not { } longValue)
                {
                    failure = $"column '{sized.Name}' ({sized.Type}) is not a string or binary column at N-1";
                    return false;
                }

                values = [new ColumnValue(sized.Name, longValue)];
                return true;

            case ScenarioKind.ColumnTypeConversionIncompatible:
            case ScenarioKind.DecimalPrecisionOverflow:
            case ScenarioKind.CheckConstraintViolation:
                if (!FindColumn(scenario, table, out var planned, out failure))
                {
                    return false;
                }

                if (scenario.SeedValue is null)
                {
                    failure = scenario.Kind == ScenarioKind.CheckConstraintViolation
                        ? "no value is known that breaks this check constraint"
                        : "no value is known that fails this conversion";
                    return false;
                }

                values = [new ColumnValue(planned.Name, scenario.SeedValue)];
                return true;

            case ScenarioKind.ForeignKeyOrphan:
                var orphan = new List<ColumnValue>();
                foreach (string name in scenario.Columns)
                {
                    if (table.FindColumn(name) is not { } column)
                    {
                        failure = $"column '{name}' does not exist at N-1";
                        return false;
                    }

                    if (SqlLiterals.OrphanValue(column.Type) is not { } value)
                    {
                        failure = $"no orphan value can be generated for column '{name}' of type {column.Type}";
                        return false;
                    }

                    orphan.Add(new ColumnValue(column.Name, value));
                }

                values = orphan;
                return true;

            default:
                return true;
        }
    }

    private static bool FindColumn(
        MigrationScenario scenario,
        TableMetadata table,
        [NotNullWhen(true)] out ColumnMetadata? column,
        [NotNullWhen(false)] out string? failure)
    {
        string name = scenario.Columns.Count > 0 ? scenario.Columns[0] : "";
        column = table.FindColumn(name);
        failure = column is null ? $"column '{name}' does not exist at N-1" : null;
        return column is not null;
    }
}
