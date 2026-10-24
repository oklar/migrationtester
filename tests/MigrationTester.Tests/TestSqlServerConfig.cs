using Microsoft.Data.SqlClient;

namespace MigrationTester.Tests;

/// <summary>Connection strings for local SQL Server (SSMS). Override with env vars in CI.</summary>
public static class TestSqlServerConfig
{
    /// <summary>Used by <c>dotnet ef</c> design-time factory.</summary>
    public static string DesignTimeConnectionString =>
        Environment.GetEnvironmentVariable("MIGRATION_TESTER_SQL_DESIGN")
        ?? "Server=localhost;Database=MigrationTester_Design;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    /// <summary>
    /// Used by tests. Its database name is the base for the runner's throwaway databases
    /// (<c>MigrationTester_Test_mt_…</c>) and the fixed name tests pass as <c>DatabaseName</c>.
    /// </summary>
    public static string TestRunConnectionString =>
        Environment.GetEnvironmentVariable("MIGRATION_TESTER_SQL_TEST")
        ?? "Server=localhost;Database=MigrationTester_Test;Integrated Security=True;TrustServerCertificate=True;MultipleActiveResultSets=True";

    public static string TestDatabaseName => new SqlConnectionStringBuilder(TestRunConnectionString).InitialCatalog;

    public static async Task<bool> DatabaseExistsAsync(string name, CancellationToken cancellationToken)
    {
        await using var connection = await OpenMasterAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(*) FROM sys.databases WHERE name = @name;";
        cmd.Parameters.AddWithValue("@name", name);
        return (int)(await cmd.ExecuteScalarAsync(cancellationToken))! > 0;
    }

    /// <summary>The fixed test database and any throwaway databases derived from it.</summary>
    public static async Task<IReadOnlyList<string>> TestDatabasesAsync(CancellationToken cancellationToken)
    {
        await using var connection = await OpenMasterAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sys.databases WHERE name = @name OR name LIKE @throwaway;";
        cmd.Parameters.AddWithValue("@name", TestDatabaseName);
        cmd.Parameters.AddWithValue("@throwaway", EscapeLike(TestDatabaseName) + "[_]mt[_]%");
        var names = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            names.Add(reader.GetString(0));
        }

        return names;
    }

    public static async Task DropDatabaseAsync(string name, CancellationToken cancellationToken)
    {
        string quoted = "[" + name.Replace("]", "]]", StringComparison.Ordinal) + "]";
        await using var connection = await OpenMasterAsync(cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = $"IF DB_ID(@name) IS NOT NULL BEGIN ALTER DATABASE {quoted} SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE {quoted}; END";
        cmd.Parameters.AddWithValue("@name", name);
        await cmd.ExecuteNonQueryAsync(cancellationToken);
    }

    public static async Task<T> ScalarAsync<T>(string database, string sql, CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(database, cancellationToken);
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = sql;
        return (T)(await cmd.ExecuteScalarAsync(cancellationToken))!;
    }

    private static Task<SqlConnection> OpenMasterAsync(CancellationToken cancellationToken) => OpenAsync("master", cancellationToken);

    private static async Task<SqlConnection> OpenAsync(string database, CancellationToken cancellationToken)
    {
        var builder = new SqlConnectionStringBuilder(TestRunConnectionString) { InitialCatalog = database };
        var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync(cancellationToken);
        return connection;
    }

    private static string EscapeLike(string value) =>
        value.Replace("[", "[[]", StringComparison.Ordinal).Replace("_", "[_]", StringComparison.Ordinal).Replace("%", "[%]", StringComparison.Ordinal);
}
