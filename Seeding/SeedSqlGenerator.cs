using System.Text;
using MigrationRepair.Planning;
using MigrationRepair.Schema;

namespace MigrationRepair.Seeding;

public static class SeedSqlGenerator
{
    public static IReadOnlyList<string> BaselineInserts(RelationalSchemaModel schema)
    {
        var order = TableInsertOrder.Topological(schema.TableNames, schema.ForeignKeys);
        var sql = new List<string>();
        foreach (string table in order)
        {
            if (!schema.ColumnsByTable.TryGetValue(table, out var cols))
            {
                continue;
            }

            string? stmt = TryBuildInsertOneRow(table, cols);
            if (stmt is not null)
            {
                sql.Add(stmt);
            }
        }

        return sql;
    }

    public static IReadOnlyList<string> AdversarialStatements(
        AdversaryScenario scenario,
        RelationalSchemaModel schema)
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
            AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint => DuplicateRows(scenario, schema, cols),
            AdversaryScenarioKind.ForeignKeyOrphan => OrphanRow(scenario, schema, cols),
            AdversaryScenarioKind.NonNullableColumnWithNull => NullOutColumn(scenario, schema, cols),
            AdversaryScenarioKind.MaxLengthExceededAfterShrink => LongValue(scenario, schema, cols),
            _ => []
        };
    }

    private static IReadOnlyList<string> DuplicateRows(
        AdversaryScenario s,
        RelationalSchemaModel schema,
        IReadOnlyList<TableColumnMetadata> cols)
    {
        string dupValue = "adv-dup";
        string t = s.Table;
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
                string lit = DefaultLiteral(c, seed: 1);
                vals1.Add(lit);
                vals2.Add(AdjustLiteralForSecondRow(c, lit));
            }
        }

        if (insertCols.Count == 0)
        {
            return [];
        }

        string tableQ = SqlDialect.Q(t);
        string colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c)));
        return
        [
            $"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals1)});",
            $"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals2)});"
        ];
    }

    private static string AdjustLiteralForSecondRow(TableColumnMetadata c, string firstLit)
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
        IReadOnlyList<TableColumnMetadata> cols)
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
            int fkIdx = IndexOfColumn(s.Columns, c.Name);
            if (fkIdx >= 0 && s.PrincipalColumns is { Count: > 0 })
            {
                vals.Add(OrphanFkLiteral(c));
            }
            else
            {
                vals.Add(DefaultLiteral(c, seed: 99));
            }
        }

        if (insertCols.Count == 0)
        {
            return [];
        }

        string tableQ = SqlDialect.Q(s.Table);
        string colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c)));
        return [$"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals)});"];
    }

    private static int IndexOfColumn(IReadOnlyList<string> columns, string name)
    {
        for (int i = 0; i < columns.Count; i++)
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
        IReadOnlyList<TableColumnMetadata> cols)
    {
        if (s.Columns is not { Count: > 0 })
        {
            return [];
        }

        string target = s.Columns[0];
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
                vals.Add(DefaultLiteral(c, seed: 2));
            }
        }

        if (insertCols.Count == 0)
        {
            return [];
        }

        string tableQ = SqlDialect.Q(s.Table);
        string colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c)));
        return [$"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals)});"];
    }

    private static IReadOnlyList<string> LongValue(
        AdversaryScenario s,
        RelationalSchemaModel schema,
        IReadOnlyList<TableColumnMetadata> cols)
    {
        if (s.Columns is not { Count: > 0 })
        {
            return [];
        }

        int newMax = s.NewMaxLength ?? 1;
        string payload = new string('x', Math.Max(newMax + 16, newMax * 2));
        string tableQ = SqlDialect.Q(s.Table);
        string colQ = SqlDialect.Q(s.Columns[0]);
        return [$"UPDATE {tableQ} SET {colQ} = {SqlString(payload)};"];
    }

    private static string? TryBuildInsertOneRow(
        string table,
        IReadOnlyList<TableColumnMetadata> cols)
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
                vals.Add(DefaultLiteral(c, seed: 1));
            }
        }

        if (insertCols.Count == 0)
        {
            return null;
        }

        string tableQ = SqlDialect.Q(table);
        string colList = string.Join(", ", insertCols.Select(c => SqlDialect.Q(c)));
        return $"INSERT INTO {tableQ} ({colList}) VALUES ({string.Join(", ", vals)});";
    }

    private static string DefaultLiteral(TableColumnMetadata c, int seed)
    {
        string t = c.DeclaredType ?? "";
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

        if (string.Equals(t, "bit", StringComparison.OrdinalIgnoreCase))
        {
            return "0";
        }

        if (t.Contains("binary", StringComparison.OrdinalIgnoreCase))
        {
            return "0x00";
        }

        if (t.Contains("BLOB", StringComparison.OrdinalIgnoreCase))
        {
            return "0x00";
        }

        if (string.Equals(t, "date", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "datetime", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "datetime2", StringComparison.OrdinalIgnoreCase)
            || string.Equals(t, "smalldatetime", StringComparison.OrdinalIgnoreCase))
        {
            return "'2000-01-01'";
        }

        return SqlString($"s{seed}");
    }

    private static string SqlString(string s)
    {
        var sb = new StringBuilder("N'");
        foreach (char ch in s)
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
