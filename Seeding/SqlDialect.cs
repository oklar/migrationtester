namespace MigrationRepair.Seeding;

public static class SqlDialect
{
    /// <summary>Bracket-quoted SQL Server identifier (dbo-style).</summary>
    public static string Q(string identifier)
    {
        return "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]";
    }
}
