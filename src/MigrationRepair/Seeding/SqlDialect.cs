using MigrationRepair.Schema;

namespace MigrationRepair.Seeding;

internal static class SqlDialect
{
    /// <summary>Bracket-quoted SQL Server identifier (dbo-style).</summary>
    public static string Q(string identifier)
    {
        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }

    /// <summary>Schema-qualified, bracket-quoted table name.</summary>
    public static string Q(TableName table) => $"{Q(table.Schema)}.{Q(table.Name)}";

    /// <summary>Table reference, schema-qualified when the schema is known.</summary>
    public static string Q(string? schema, string table) => schema is null ? Q(table) : Q(new TableName(schema, table));
}
