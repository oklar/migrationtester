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
            AdversaryScenarioKind.ColumnTypeConversionIncompatible => IncompatibleValue(scenario),
            AdversaryScenarioKind.DecimalPrecisionOverflow => OverflowValue(scenario),
            _ => []
        };
    }

    private static IReadOnlyList<string> DuplicateRows(
        AdversaryScenario s,
        RelationalSchemaModel schema,
        IReadOnlyList<TableColumnMetadata> cols)
    {
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
                string dup = DuplicateLiteral(c);
                vals1.Add(dup);
                vals2.Add(dup);
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
        string normalized = NormalizeTypeName(c.DeclaredType ?? "");
        if (normalized is "TINYINT" or "SMALLINT" or "INT" or "BIGINT" && firstLit == "0")
        {
            return "1";
        }

        if (firstLit.StartsWith('\'') && firstLit.EndsWith('\''))
        {
            return SqlString(firstLit.Trim('\'') + "-b");
        }

        return firstLit;
    }

    private static string DuplicateLiteral(TableColumnMetadata c)
    {
        string normalized = NormalizeTypeName(c.DeclaredType ?? "");
        return normalized switch
        {
            "TINYINT" or "SMALLINT" or "INT" or "BIGINT" => "42",
            "REAL" or "FLOAT" or "DOUBLE" => "42.0",
            "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY" => "42",
            "BIT" => "1",
            "UNIQUEIDENTIFIER" => "'AAAAAAAA-AAAA-AAAA-AAAA-AAAAAAAAAAAA'",
            "DATE" or "DATETIME" or "DATETIME2" or "SMALLDATETIME" => "'2000-01-01'",
            "TIME" => "'00:00:00'",
            "DATETIMEOFFSET" => "'2000-01-01T00:00:00+00:00'",
            "BINARY" or "VARBINARY" or "IMAGE" => "0xDEAD",
            _ => SqlString("adv-dup")
        };
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
        string normalized = NormalizeTypeName(c.DeclaredType ?? "");
        return normalized switch
        {
            "TINYINT" or "SMALLINT" or "INT" or "BIGINT" => "999999999",
            "UNIQUEIDENTIFIER" => "'00000000-0000-0000-0000-000000000099'",
            _ => "'__orphan_fk__'"
        };
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
        var targetCol = cols.FirstOrDefault(c => string.Equals(c.Name, target, StringComparison.OrdinalIgnoreCase));
        if (targetCol is not { IsNullable: true })
        {
            return [];
        }

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

    private static IReadOnlyList<string> IncompatibleValue(AdversaryScenario s)
    {
        if (s.Columns is not { Count: > 0 } || s.NewColumnType is null)
        {
            return [];
        }

        string tableQ = SqlDialect.Q(s.Table);
        string colQ = SqlDialect.Q(s.Columns[0]);
        string poison = PoisonLiteral(s.OldColumnType, s.NewColumnType);
        return [$"UPDATE {tableQ} SET {colQ} = {poison};"];
    }

    internal static string PoisonLiteral(string? oldType, string newType)
    {
        string nt = NormalizeTypeName(newType);
        string ot = NormalizeTypeName(oldType ?? "");

        if (nt is "INT" or "BIGINT" or "SMALLINT" or "TINYINT")
        {
            if (IsStringType(ot))
            {
                return SqlString("__not_a_number__");
            }

            return ot is "FLOAT" or "REAL" ? "1.5" : SqlString("__bad__");
        }

        if (nt is "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY")
        {
            return IsStringType(ot) ? SqlString("__not_numeric__") : SqlString("__bad__");
        }

        if (nt == "UNIQUEIDENTIFIER")
        {
            return SqlString("__not_a_guid__");
        }

        if (nt is "DATE" or "DATETIME" or "DATETIME2" or "SMALLDATETIME" or "DATETIMEOFFSET" or "TIME")
        {
            return SqlString("__not_a_date__");
        }

        if (nt == "BIT")
        {
            return IsStringType(ot) ? SqlString("maybe") : SqlString("__bad__");
        }

        return SqlString("__unconvertible__");
    }

    private static IReadOnlyList<string> OverflowValue(AdversaryScenario s)
    {
        if (s.Columns is not { Count: > 0 } || s.NewPrecision is not int newP)
        {
            return [];
        }

        int newScale = s.NewScale ?? 0;
        int intDigits = newP - newScale;

        string overflow = intDigits > 0
            ? new string('9', intDigits + 1) + (newScale > 0 ? "." + new string('9', newScale) : "")
            : "1" + new string('0', newScale + 1);

        string tableQ = SqlDialect.Q(s.Table);
        string colQ = SqlDialect.Q(s.Columns[0]);
        return [$"UPDATE {tableQ} SET {colQ} = {overflow};"];
    }

    private static string NormalizeTypeName(string sqlType)
    {
        string t = sqlType.Trim();
        int paren = t.IndexOf('(');
        if (paren > 0)
        {
            t = t[..paren].Trim();
        }

        return t.ToUpperInvariant();
    }

    private static bool IsStringType(string normalized)
        => normalized is "NVARCHAR" or "VARCHAR" or "NCHAR" or "CHAR"
            or "TEXT" or "NTEXT" or "XML";

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
        string normalized = NormalizeTypeName(c.DeclaredType ?? "");
        return normalized switch
        {
            "TINYINT" or "SMALLINT" or "INT" or "BIGINT" => seed.ToString(),
            "REAL" or "FLOAT" or "DOUBLE" => seed.ToString(),
            "DECIMAL" or "NUMERIC" or "MONEY" or "SMALLMONEY" => seed.ToString(),
            "BIT" => "0",
            "BINARY" or "VARBINARY" or "IMAGE" => "0x00",
            "UNIQUEIDENTIFIER" => $"'{new Guid(0, 0, 0, 0, 0, 0, 0, 0, 0, 0, (byte)seed)}'",
            "TIME" => "'00:00:00'",
            "DATETIMEOFFSET" => "'2000-01-01T00:00:00+00:00'",
            "DATE" or "DATETIME" or "DATETIME2" or "SMALLDATETIME" => "'2000-01-01'",
            _ => SqlString($"s{seed}")
        };
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
