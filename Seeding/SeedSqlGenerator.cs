using System.Text;
using MigrationRepair.Hints;
using MigrationRepair.Planning;
using MigrationRepair.Schema;

namespace MigrationRepair.Seeding;

public static class SeedSqlGenerator
{
    public static IReadOnlyList<string> BaselineInserts(RelationalSchemaModel schema, DatabaseFamily family)
    {
        var order = TableInsertOrder.Topological(schema.TableNames, schema.ForeignKeys);
        var sql = new List<string>();
        foreach (var table in order)
        {
            if (!schema.ColumnsByTable.TryGetValue(table, out var cols))
            {
                continue;
            }

            var stmt = TryBuildInsertOneRow(table, cols, family);
            if (stmt is not null)
            {
                sql.Add(stmt);
            }
        }

        return sql;
    }

    public static IReadOnlyList<string> AdversarialStatements(
        AdversaryScenario scenario,
        RelationalSchemaModel schema,
        DatabaseFamily family)
    {
        if (scenario.Skipped)
        {
            return [];
        }

        if (!schema.ColumnsByTable.TryGetValue(scenario.Table, out var cols))
        {
            return [];
        }

        return scenario.Kind switch
        {
            AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint => DuplicateRows(scenario, schema, cols, family),
            AdversaryScenarioKind.ForeignKeyOrphan => OrphanRow(scenario, schema, cols, family),
            AdversaryScenarioKind.NonNullableColumnWithNull => NullOutColumn(scenario, schema, cols, family),
            AdversaryScenarioKind.MaxLengthExceededAfterShrink => LongValue(scenario, schema, cols, family),
            _ => []
        };
    }

    private static IReadOnlyList<string> DuplicateRows(
        AdversaryScenario s,
        RelationalSchemaModel schema,
        IReadOnlyList<TableColumnMetadata> cols,
        DatabaseFamily family)
    {
        var dupValue = "adv-dup";
        var t = s.Table;
        var insertCols = new List<string>();
        var vals1 = new List<string>();
        var vals2 = new List<string>();

        foreach (var c in cols.OrderBy(static x => x.Ordinal))
        {
            if (c.IsPrimaryKey && c.IsAutoIncrement)
            {
                continue;
            }

            insertCols.Add(c.Name);
            if (s.Columns.Any(u => string.Equals(u, c.Name, StringComparison.OrdinalIgnoreCase)))
            {
                vals1.Add(SqlString(dupValue));
                vals2.Add(SqlString(dupValue));
            }
            else if (c.IsPrimaryKey)
            {
                vals1.Add("1");
                vals2.Add("2");
            }
            else
            {
                var lit = DefaultLiteral(c, family, seed: 1);
                vals1.Add(lit);
                vals2.Add(AdjustLiteralForSecondRow(c, lit, family));
            }
        }

        if (insertCols.Count == 0)
        {
            return [];
        }

        var tableQ = SqlDialect.Q(t, family);
        var colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c, family)));
        return
        [
            $"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals1)});",
            $"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals2)});"
        ];
    }

    private static string AdjustLiteralForSecondRow(TableColumnMetadata c, string firstLit, DatabaseFamily family)
    {
        if (c.DeclaredType?.Contains("INT", StringComparison.OrdinalIgnoreCase) == true && firstLit == "0")
        {
            return "1";
        }

        if (firstLit.StartsWith('\'') && firstLit.EndsWith('\''))
        {
            return SqlString(firstLit.Trim('\'') + "-b");
        }

        return firstLit;
    }

    private static IReadOnlyList<string> OrphanRow(
        AdversaryScenario s,
        RelationalSchemaModel schema,
        IReadOnlyList<TableColumnMetadata> cols,
        DatabaseFamily family)
    {
        var insertCols = new List<string>();
        var vals = new List<string>();
        foreach (var c in cols.OrderBy(static x => x.Ordinal))
        {
            if (c.IsPrimaryKey && c.IsAutoIncrement)
            {
                continue;
            }

            insertCols.Add(c.Name);
            var fkIdx = IndexOfColumn(s.Columns, c.Name);
            if (fkIdx >= 0 && s.PrincipalColumns is { Count: > 0 })
            {
                vals.Add(OrphanFkLiteral(c));
            }
            else
            {
                vals.Add(DefaultLiteral(c, family, seed: 99));
            }
        }

        if (insertCols.Count == 0)
        {
            return [];
        }

        var tableQ = SqlDialect.Q(s.Table, family);
        var colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c, family)));
        return [$"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals)});"];
    }

    private static int IndexOfColumn(IReadOnlyList<string> columns, string name)
    {
        for (var i = 0; i < columns.Count; i++)
        {
            if (string.Equals(columns[i], name, StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }

    private static string OrphanFkLiteral(TableColumnMetadata c)
    {
        if (c.DeclaredType?.Contains("INT", StringComparison.OrdinalIgnoreCase) == true
            || c.DeclaredType?.Contains("BIGINT", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "999999999";
        }

        if (c.DeclaredType?.Contains("uniqueidentifier", StringComparison.OrdinalIgnoreCase) == true
            || c.DeclaredType?.Contains("GUID", StringComparison.OrdinalIgnoreCase) == true)
        {
            return "'00000000-0000-0000-0000-000000000099'";
        }

        return "'__orphan_fk__'";
    }

    private static IReadOnlyList<string> NullOutColumn(
        AdversaryScenario s,
        RelationalSchemaModel schema,
        IReadOnlyList<TableColumnMetadata> cols,
        DatabaseFamily family)
    {
        if (s.Columns is not { Count: > 0 })
        {
            return [];
        }

        var target = s.Columns[0];
        var insertCols = new List<string>();
        var vals = new List<string>();
        foreach (var c in cols.OrderBy(static x => x.Ordinal))
        {
            if (c.IsPrimaryKey && c.IsAutoIncrement)
            {
                continue;
            }

            insertCols.Add(c.Name);
            if (string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase))
            {
                vals.Add("NULL");
            }
            else if (c.IsNullable)
            {
                vals.Add("NULL");
            }
            else
            {
                vals.Add(DefaultLiteral(c, family, seed: 2));
            }
        }

        if (insertCols.Count == 0)
        {
            return [];
        }

        var tableQ = SqlDialect.Q(s.Table, family);
        var colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c, family)));
        return [$"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals)});"];
    }

    private static IReadOnlyList<string> LongValue(
        AdversaryScenario s,
        RelationalSchemaModel schema,
        IReadOnlyList<TableColumnMetadata> cols,
        DatabaseFamily family)
    {
        if (s.Columns is not { Count: > 0 })
        {
            return [];
        }

        var newMax = s.NewMaxLength ?? 1;
        var payload = new string('x', Math.Max(newMax + 16, newMax * 2));
        var tableQ = SqlDialect.Q(s.Table, family);
        var colQ = SqlDialect.Q(s.Columns[0], family);
        return [$"UPDATE {tableQ} SET {colQ} = {SqlString(payload)};"];
    }

    private static string? TryBuildInsertOneRow(
        string table,
        IReadOnlyList<TableColumnMetadata> cols,
        DatabaseFamily family)
    {
        var insertCols = new List<string>();
        var vals = new List<string>();
        foreach (var c in cols.OrderBy(static x => x.Ordinal))
        {
            if (c.IsPrimaryKey && c.IsAutoIncrement)
            {
                continue;
            }

            insertCols.Add(c.Name);
            if (c.IsNullable)
            {
                vals.Add("NULL");
            }
            else
            {
                vals.Add(DefaultLiteral(c, family, seed: 1));
            }
        }

        if (insertCols.Count == 0)
        {
            return null;
        }

        var tableQ = SqlDialect.Q(table, family);
        var colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c, family)));
        return $"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals)});";
    }

    private static string DefaultLiteral(TableColumnMetadata c, DatabaseFamily family, int seed)
    {
        var t = c.DeclaredType ?? "";
        if (t.Contains("INT", StringComparison.OrdinalIgnoreCase)
            && !t.Contains("POINT", StringComparison.OrdinalIgnoreCase))
        {
            return seed.ToString();
        }

        if (t.Contains("REAL", StringComparison.OrdinalIgnoreCase)
            || t.Contains("FLOA", StringComparison.OrdinalIgnoreCase)
            || t.Contains("DOUBLE", StringComparison.OrdinalIgnoreCase)
            || t.Contains("DECIMAL", StringComparison.OrdinalIgnoreCase)
            || t.Contains("NUMERIC", StringComparison.OrdinalIgnoreCase))
        {
            return seed.ToString();
        }

        if (t.Contains("BLOB", StringComparison.OrdinalIgnoreCase))
        {
            return "X'00'";
        }

        return SqlString($"s{seed}");
    }

    private static string SqlString(string s)
    {
        var sb = new StringBuilder("'");
        foreach (var ch in s)
        {
            if (ch == '\'')
            {
                sb.Append("''");
            }
            else
            {
                sb.Append(ch);
            }
        }

        sb.Append('\'');
        return sb.ToString();
    }
}
