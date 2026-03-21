namespace MigrationRepair.Examples;

/// <summary>
/// Separate from the MigrationRepair library repo tests. Override with env vars for CI/Docker.
/// </summary>
public static class TestSqlConfig
{
    public static string DesignTimeConnectionString =>
        Environment.GetEnvironmentVariable("MIGRATION_REPAIR_SCENARIO_DESIGN")
        ?? "Server=localhost;Database=MigrationRepairExamples_Design;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public static string TestRunConnectionString =>
        Environment.GetEnvironmentVariable("MIGRATION_REPAIR_SCENARIO_TEST")
        ?? "Server=localhost;Database=MigrationRepairExamples_Test;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
}
