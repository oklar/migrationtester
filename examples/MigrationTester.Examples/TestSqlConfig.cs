namespace MigrationTester.Examples;

/// <summary>
/// Separate from the MigrationTester library repo tests. Override with env vars for CI/Docker.
/// </summary>
public static class TestSqlConfig
{
    public static string DesignTimeConnectionString =>
        Environment.GetEnvironmentVariable("MIGRATION_TESTER_SCENARIO_DESIGN")
        ?? "Server=localhost;Database=MigrationTesterExamples_Design;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public static string TestRunConnectionString =>
        Environment.GetEnvironmentVariable("MIGRATION_TESTER_SCENARIO_TEST")
        ?? "Server=localhost;Database=MigrationTesterExamples_Test;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";
}
