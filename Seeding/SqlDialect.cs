using MigrationRepair.Hints;

namespace MigrationRepair.Seeding;

public static class SqlDialect
{
    public static string Q(string identifier, DatabaseFamily family)
    {
        var escaped = identifier.Replace("\"", "\"\"", StringComparison.Ordinal);
        return family switch
        {
            DatabaseFamily.MySql => "`" + identifier.Replace("`", "``", StringComparison.Ordinal) + "`",
            DatabaseFamily.SqlServer => "[" + identifier.Replace("]", "]]", StringComparison.Ordinal) + "]",
            _ => "\"" + escaped + "\""
        };
    }
}
