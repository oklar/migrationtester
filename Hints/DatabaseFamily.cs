namespace MigrationRepair.Hints;

/// <summary>
/// Dialect family for SQL repair hint wording (portable snippets + engine-specific notes).
/// </summary>
public enum DatabaseFamily
{
    /// <summary>ANSI-style snippets only; notes mention verifying syntax for your engine.</summary>
    Portable = 0,

    MySql = 1,
    SqlServer = 2,
    PostgreSql = 3,
    Sqlite = 4
}
