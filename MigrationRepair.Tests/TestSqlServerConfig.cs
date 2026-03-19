namespace MigrationRepair.Tests;

/// <summary>Connection strings for local SQL Server (SSMS). Override with env vars in CI.</summary>
public static class TestSqlServerConfig
{
    /// <summary>Used by <c>dotnet ef</c> design-time factory.</summary>
    public static string DesignTimeConnectionString =>
        Environment.GetEnvironmentVariable("MIGRATION_REPAIR_SQL_DESIGN")
        ?? "Server=localhost;Database=MigrationRepair_Design;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    /// <summary>Used by tests (dropped/recreated by the adversary runner).</summary>
    public static string TestRunConnectionString =>
        Environment.GetEnvironmentVariable("MIGRATION_REPAIR_SQL_TEST")
        ?? "Server=localhost;Database=MigrationRepair_Test;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
}
