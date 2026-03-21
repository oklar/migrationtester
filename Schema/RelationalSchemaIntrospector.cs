using System.Data;
using System.Data.Common;

namespace MigrationRepair.Schema;

/// <summary>
/// Reads table/column/FK metadata from a live SQL Server database (after migrate to N-1).
/// </summary>
public static class RelationalSchemaIntrospector
{
    public static async Task<RelationalSchemaModel> LoadAsync(
        DbConnection connection,
        CancellationToken cancellationToken = default)
    {
        bool shouldClose = connection.State == ConnectionState.Closed;
        if (shouldClose)
        {
            await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        }

        try
        {
            return await LoadSqlServer(connection, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            if (shouldClose)
            {
                await connection.CloseAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<RelationalSchemaModel> LoadSqlServer(
        DbConnection connection,
        CancellationToken cancellationToken)
    {
        var tables = new List<string>();
        await using (var cmd = connection.CreateCommand())
        {
            cmd.CommandText = """
                SELECT t.name
                FROM sys.tables t
                WHERE t.schema_id = SCHEMA_ID('dbo')
                  AND t.is_ms_shipped = 0
                  AND t.name <> '__EFMigrationsHistory'
                ORDER BY t.name;
                """;
            await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                tables.Add(reader.GetString(0));
            }
        }

        var columns = new Dictionary<string, List<TableColumnMetadata>>(StringComparer.OrdinalIgnoreCase);
        foreach (string table in tables)
        {
            var list = new List<TableColumnMetadata>();
            await using (var cmd = connection.CreateCommand())
            {
                cmd.CommandText = """
                    SELECT
                        c.name,
                        c.column_id,
                        c.is_nullable,
                        ty.name AS type_name,
                        CASE WHEN pk.column_id IS NOT NULL THEN 1 ELSE 0 END AS is_pk,
                        CASE WHEN c.is_identity = 1 THEN 1 ELSE 0 END AS is_identity
                    FROM sys.columns c
                    INNER JOIN sys.tables t ON c.object_id = t.object_id
                    INNER JOIN sys.types ty ON c.user_type_id = ty.user_type_id
                    LEFT JOIN (
                        SELECT ic.object_id, ic.column_id
                        FROM sys.index_columns ic
                        INNER JOIN sys.indexes i ON i.object_id = ic.object_id AND i.index_id = ic.index_id
                        WHERE i.is_primary_key = 1
                    ) pk ON pk.object_id = c.object_id AND pk.column_id = c.column_id
                    WHERE t.schema_id = SCHEMA_ID('dbo') AND t.name = @tbl
                      AND c.is_computed = 0
                    ORDER BY c.column_id;
                    """;
                cmd.Parameters.Add(CreateParam(cmd, "@tbl", table));
                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    string colName = reader.GetString(0);
                    bool nullable = reader.GetBoolean(2);
                    string dataType = reader.GetString(3);
                    bool isPk = reader.GetInt32(4) != 0;
                    bool isIdentity = reader.GetInt32(5) != 0;
                    bool isRowVersion = string.Equals(dataType, "rowversion", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(dataType, "timestamp", StringComparison.OrdinalIgnoreCase);

                    list.Add(new TableColumnMetadata
                    {
                        TableName = table,
                        Name = colName,
                        Ordinal = list.Count,
                        IsNullable = nullable,
                        DeclaredType = dataType,
                        IsPrimaryKey = isPk,
                        IsAutoIncrement = isIdentity || isRowVersion
                    });
                }
            }

            columns[table] = list;
        }

        var fks = new List<ForeignKeyEdge>();
        await using (var cmdFk = connection.CreateCommand())
        {
            cmdFk.CommandText = """
                SELECT
                    fk.object_id,
                    OBJECT_NAME(fk.parent_object_id) AS dep_table,
                    COL_NAME(fc.parent_object_id, fc.parent_column_id) AS dep_col,
                    OBJECT_NAME(fk.referenced_object_id) AS prin_table,
                    COL_NAME(fc.referenced_object_id, fc.referenced_column_id) AS prin_col,
                    fc.constraint_column_id AS ord
                FROM sys.foreign_keys fk
                INNER JOIN sys.foreign_key_columns fc ON fk.object_id = fc.constraint_object_id
                INNER JOIN sys.tables t ON fk.parent_object_id = t.object_id
                WHERE t.schema_id = SCHEMA_ID('dbo')
                ORDER BY fk.object_id, fc.constraint_column_id;
                """;
            await using var reader = await cmdFk.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            var group = new Dictionary<int, List<(string DepTable, string From, string PrinTable, string To, int Ord)>>();
            while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                int oid = reader.GetInt32(0);
                string depTable = reader.GetString(1);
                string depCol = reader.GetString(2);
                string prinTable = reader.GetString(3);
                string prinCol = reader.GetString(4);
                int ord = reader.GetInt32(5);
                if (!group.TryGetValue(oid, out var rows))
                {
                    rows = [];
                    group[oid] = rows;
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

    private static DbParameter CreateParam(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        return p;
    }
}
