using System.Data;
using System.Data.Common;
using Microsoft.Data.Sqlite;
using MigrationRepair.Hints;

namespace MigrationRepair.Schema;

/// <summary>
/// Reads table/column/FK metadata from a live database (after migrate to N-1).
/// </summary>
public static class RelationalSchemaIntrospector
{
    /// <summary>
    /// Introspects SQLite using a dedicated connection (avoids interfering with EF's pooled connection).
    /// </summary>
    public static async Task<RelationalSchemaModel> LoadSqliteFromConnectionStringAsync(
        string connectionString,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(connectionString);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return await LoadSqlite(connection, cancellationToken).ConfigureAwait(false);
    }

    public static async Task<RelationalSchemaModel> LoadAsync(
        DbConnection connection,
        DatabaseFamily family,
        CancellationToken cancellationToken = default)
    {
        var shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return family switch
            {
                DatabaseFamily.Sqlite => await LoadSqlite(connection, cancellationToken).ConfigureAwait(false),
                DatabaseFamily.MySql => await LoadMySql(connection, cancellationToken).ConfigureAwait(false),
                _ => throw new NotSupportedException(
                    $"RelationalSchemaIntrospector.LoadAsync does not support {family}. Use Sqlite or MySql, or call LoadSqliteFromConnectionStringAsync for SQLite file connections.")
            };
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<RelationalSchemaModel> LoadSqlite(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT name FROM sqlite_master
                WHERE type='table' AND name NOT LIKE 'sqlite_%' AND name != '__EFMigrationsHistory'
                ORDER BY name;
                """;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var columns = new Dictionary<string, List<TableColumnMetadata>>(StringComparer.OrdinalIgnoreCase);
        var fks = new List<ForeignKeyEdge>();

        foreach (var table in tables)
        {
            var list = new List<TableColumnMetadata>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA table_info({QuoteSqliteIdent(table)});";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // cid, name, type, notnull, dflt_value, pk
                    var name = reader.GetString(1);
                    var type = reader.FieldCount > 2 && !reader.IsDBNull(2) ? reader.GetString(2) : null;
                    var notNull = reader.GetInt64(3) == 1;
                    var pk = reader.FieldCount > 5 && reader.GetInt64(5) == 1;
                    list.Add(new TableColumnMetadata
                    {
                        TableName = table,
                        Name = name,
                        Ordinal = list.Count,
                        IsNullable = !notNull,
                        DeclaredType = type,
                        IsPrimaryKey = pk,
                        IsAutoIncrement = pk && type is not null &&
                            type.Contains("INT", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }

            columns[table] = list;

            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = $"PRAGMA foreign_key_list({QuoteSqliteIdent(table)});";
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                var fkGroups = new Dictionary<long, (string Principal, List<(int Seq, string From, string To)> Cols)>();
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    // id, seq, table, from, to, on_update, on_delete, match
                    var fkId = reader.GetInt64(0);
                    var seq = (int)reader.GetInt64(1);
                    var principal = reader.GetString(2);
                    var from = reader.GetString(3);
                    var to = reader.GetString(4);
                    if (!fkGroups.TryGetValue(fkId, out var g))
                    {
                        g = (principal, []);
                        fkGroups[fkId] = g;
                    }

                    g.Cols.Add((seq, from, to));
                }

                foreach (var (_, (principal, cols)) in fkGroups)
                {
                    var ordered = cols.OrderBy(static x => x.Seq).ToList();
                    fks.Add(new ForeignKeyEdge
                    {
                        DependentTable = table,
                        PrincipalTable = principal,
                        DependentColumns = ordered.Select(static x => x.From).ToList(),
                        PrincipalColumns = ordered.Select(static x => x.To).ToList()
                    });
                }
            }
        }

        return new RelationalSchemaModel
        {
            TableNames = tables,
            ColumnsByTable = columns.ToDictionary(static kv => kv.Key, static kv => (IReadOnlyList<TableColumnMetadata>)kv.Value),
            ForeignKeys = fks
        };
    }

    private static async Task<RelationalSchemaModel> LoadMySql(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var dbName = connection.Database;
        var tables = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT TABLE_NAME FROM information_schema.TABLES
                WHERE TABLE_SCHEMA = @db AND TABLE_TYPE = 'BASE TABLE'
                  AND TABLE_NAME != '__EFMigrationsHistory'
                ORDER BY TABLE_NAME;
                """;
            var p = cmd.CreateParameter();
            p.ParameterName = "@db";
            p.Value = dbName;
            cmd.Parameters.Add(p);
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var columns = new Dictionary<string, List<TableColumnMetadata>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            var list = new List<TableColumnMetadata>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT COLUMN_NAME, ORDINAL_POSITION, IS_NULLABLE, DATA_TYPE, COLUMN_KEY, EXTRA
                    FROM information_schema.COLUMNS
                    WHERE TABLE_SCHEMA = @db AND TABLE_NAME = @tbl
                    ORDER BY ORDINAL_POSITION;
                    """;
                AddParams(cmd, dbName, table);
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    var colName = reader.GetString(0);
                    var nullable = string.Equals(reader.GetString(2), "YES", StringComparison.OrdinalIgnoreCase);
                    var dataType = reader.GetString(3);
                    var colKey = reader.GetString(4);
                    var extra = reader.GetString(5);
                    list.Add(new TableColumnMetadata
                    {
                        TableName = table,
                        Name = colName,
                        Ordinal = list.Count,
                        IsNullable = nullable,
                        DeclaredType = dataType,
                        IsPrimaryKey = string.Equals(colKey, "PRI", StringComparison.OrdinalIgnoreCase),
                        IsAutoIncrement = extra.Contains("auto_increment", StringComparison.OrdinalIgnoreCase)
                    });
                }
            }

            columns[table] = list;
        }

        var fks = new List<ForeignKeyEdge>();
        await using (var cmdFk = connection.CreateCommand())
        {
            cmdFk.CommandText = """
                SELECT k.CONSTRAINT_NAME, k.TABLE_NAME, k.COLUMN_NAME, k.REFERENCED_TABLE_NAME, k.REFERENCED_COLUMN_NAME, k.ORDINAL_POSITION
                FROM information_schema.KEY_COLUMN_USAGE k
                WHERE k.TABLE_SCHEMA = @db
                  AND k.REFERENCED_TABLE_NAME IS NOT NULL
                ORDER BY k.CONSTRAINT_NAME, k.ORDINAL_POSITION;
                """;
            cmdFk.Parameters.Add(CreateParam(cmdFk, "@db", dbName));
            await using var reader = await cmdFk.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var group = new Dictionary<string, List<(string DepTable, string From, string PrinTable, string To, int Ord)>>(
                StringComparer.OrdinalIgnoreCase);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                var cname = reader.GetString(0);
                var depTable = reader.GetString(1);
                var depCol = reader.GetString(2);
                var prinTable = reader.GetString(3);
                var prinCol = reader.GetString(4);
                var ord = reader.GetInt32(5);
                if (!group.TryGetValue(cname, out var rows))
                {
                    rows = [];
                    group[cname] = rows;
                }

                rows.Add((depTable, depCol, prinTable, prinCol, ord));
            }

            foreach (var rows in group.Values)
            {
                var first = rows[0];
                var ordered = rows.OrderBy(static r => r.Ord).ToList();
                fks.Add(new ForeignKeyEdge
                {
                    DependentTable = first.DepTable,
                    PrincipalTable = first.PrinTable,
                    DependentColumns = ordered.Select(static r => r.From).ToList(),
                    PrincipalColumns = ordered.Select(static r => r.To).ToList()
                });
            }
        }

        return new RelationalSchemaModel
        {
            TableNames = tables,
            ColumnsByTable = columns.ToDictionary(static kv => kv.Key, static kv => (IReadOnlyList<TableColumnMetadata>)kv.Value),
            ForeignKeys = fks
        };
    }

    private static void AddParams(DbCommand cmd, string db, string table)
    {
        cmd.Parameters.Add(CreateParam(cmd, "@db", db));
        cmd.Parameters.Add(CreateParam(cmd, "@tbl", table));
    }

    private static DbParameter CreateParam(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }

    private static string QuoteSqliteIdent(string name)
        => "\"" + name.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
}
