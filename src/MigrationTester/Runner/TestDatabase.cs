using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MigrationTester.Runner;

/// <summary>
/// The database a run drops and recreates for every migration: a throwaway database derived from the
/// configured connection string, or the explicitly named one.
/// </summary>
internal sealed class TestDatabase
{
    private const int MaxBaseNameLength = 100;

    private readonly string _connectionString;

    private TestDatabase(string name, string connectionString)
    {
        Name = name;
        _connectionString = connectionString;
    }

    public string Name { get; }

    public static TestDatabase For(DbContext context, string? databaseName)
    {
        string connectionString = context.Database.GetConnectionString()
            ?? throw new InvalidOperationException(
                "The context has no connection string. Configure it with UseSqlServer(connectionString) so MigrationTester can point it at a test database.");

        var builder = new SqlConnectionStringBuilder(connectionString);
        if (!string.IsNullOrEmpty(builder.AttachDBFilename))
        {
            throw new NotSupportedException(
                "Connection strings with AttachDbFilename are not supported; use a server connection string (LocalDB works with Server=(localdb)\\MSSQLLocalDB).");
        }

        string name = databaseName ?? ThrowawayName(builder.InitialCatalog);
        builder.InitialCatalog = name;
        return new TestDatabase(name, builder.ConnectionString);
    }

    /// <summary>A context from <paramref name="createContext"/>, pointed at this database.</summary>
    public DbContext CreateContext(Func<DbContext> createContext)
    {
        var context = createContext();
        context.Database.SetConnectionString(_connectionString);
        return context;
    }

    /// <summary>Drops the database; returns null on success, otherwise the error.</summary>
    public async Task<string?> TryDropAsync(Func<DbContext> createContext)
    {
        try
        {
            await using var context = CreateContext(createContext);
            await context.Database.EnsureDeletedAsync(CancellationToken.None).ConfigureAwait(false);
            return null;
        }
        catch (Exception ex)
        {
            return ex.Message;
        }
    }

    private static string ThrowawayName(string? configured)
    {
        string baseName = string.IsNullOrWhiteSpace(configured) ? "MigrationTester" : configured;
        if (baseName.Length > MaxBaseNameLength)
        {
            baseName = baseName[..MaxBaseNameLength];
        }

        return $"{baseName}_mt_{Guid.NewGuid().ToString("N")[..8]}";
    }
}
