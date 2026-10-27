using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Schema;

namespace MigrationTester.Planning;

/// <summary>
/// Follows the table and column renames in one <c>Up()</c>, in order, so the names later operations use map
/// back to N-1. EF scaffolds RenameColumn and RenameTable before the operations that use the new names, but
/// the data is planted before the migration runs, under the N-1 names. Index renames need nothing: no
/// scenario looks an index up by name. Also records the columns earlier AddColumn operations create, which
/// have no N-1 name: existing rows get NULL, the default or a generated value in them.
/// </summary>
internal sealed class RenameMap(RelationalSchemaModel schema)
{
    // Name now -> name at N-1, or null where an earlier rename moved the N-1 table of that name away.
    private readonly Dictionary<TableName, TableName?> _tables = [];

    // N-1 table -> column name now -> column name at N-1, or null as for tables.
    private readonly Dictionary<TableName, Dictionary<string, string?>> _columns = [];

    // N-1 table -> column name now -> the AddColumn earlier in this Up() that created it.
    private readonly Dictionary<TableName, Dictionary<string, AddColumnOperation>> _added = [];

    public RelationalSchemaModel Schema => schema;

    public void Rename(RenameTableOperation op)
    {
        var from = schema.Resolve(op.Schema, op.Name);
        var before = TableAtN1(from);
        string newName = op.NewName ?? op.Name;
        _tables[from] = null;
        _tables[schema.Resolve(op.NewSchema, newName)] = before;

        // No new schema: EF keeps the old one for a migration without a target model, and moves the table to
        // the default schema for one with a model. Later operations name only one of the two.
        if (op.NewSchema is null && op.Schema is not null)
        {
            _tables[schema.Resolve(op.Schema, newName)] = before;
        }
    }

    public void Rename(RenameColumnOperation op)
    {
        if (TableAtN1(op.Schema, op.Table) is not { } table)
        {
            return;
        }

        // A column this migration added keeps being an added column under its new name.
        if (_added.TryGetValue(table, out var added) && added.Remove(op.Name, out var addColumn))
        {
            added[op.NewName] = addColumn;
            return;
        }

        if (!_columns.TryGetValue(table, out var columns))
        {
            columns = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
            _columns[table] = columns;
        }

        string? before = columns.TryGetValue(op.Name, out string? mapped) ? mapped : op.Name;
        columns[op.Name] = null;
        columns[op.NewName] = before;
    }

    public void Add(AddColumnOperation op)
    {
        // Under a name an earlier rename freed, the table is one this migration creates.
        if (TableAtN1(op.Schema, op.Table) is not { } table)
        {
            return;
        }

        if (!_added.TryGetValue(table, out var added))
        {
            added = new Dictionary<string, AddColumnOperation>(StringComparer.OrdinalIgnoreCase);
            _added[table] = added;
        }

        added[op.Name] = op;
    }

    /// <summary>The AddColumn earlier in this migration that created <paramref name="column"/> in <paramref name="table"/> (an N-1 name).</summary>
    public AddColumnOperation? AddedColumn(TableName table, string column)
        => _added.TryGetValue(table, out var added) ? added.GetValueOrDefault(column) : null;

    /// <summary>The N-1 name of a table as an operation names it; null when an earlier rename moved the N-1 table of that name away.</summary>
    public TableName? TableAtN1(string? schemaName, string table) => TableAtN1(schema.Resolve(schemaName, table));

    /// <summary>
    /// The N-1 name of a column as an operation names it, in <paramref name="table"/> (an N-1 name); null when an
    /// earlier rename moved the N-1 column of that name away.
    /// </summary>
    public string? ColumnAtN1(TableName table, string column)
        => _columns.TryGetValue(table, out var columns) && columns.TryGetValue(column, out string? before) ? before : column;

    private TableName? TableAtN1(TableName now) => _tables.TryGetValue(now, out var before) ? before : now;
}
