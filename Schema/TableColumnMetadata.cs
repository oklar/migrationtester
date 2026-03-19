namespace MigrationRepair.Schema;

public sealed class TableColumnMetadata
{
    public required string TableName { get; init; }
    public required string Name { get; init; }
    public int Ordinal { get; init; }
    public bool IsNullable { get; init; }
    public string? DeclaredType { get; init; }
    public bool IsPrimaryKey { get; init; }
    public bool IsAutoIncrement { get; init; }
}

public sealed class ForeignKeyEdge
{
    public required string DependentTable { get; init; }
    public required string PrincipalTable { get; init; }
    public required IReadOnlyList<string> DependentColumns { get; init; }
    public required IReadOnlyList<string> PrincipalColumns { get; init; }
}

public sealed class RelationalSchemaModel
{
    public required IReadOnlyList<string> TableNames { get; init; }
    public required IReadOnlyDictionary<string, IReadOnlyList<TableColumnMetadata>> ColumnsByTable { get; init; }
    public required IReadOnlyList<ForeignKeyEdge> ForeignKeys { get; init; }
}
