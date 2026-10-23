using Microsoft.EntityFrameworkCore;

namespace MigrationTester;

/// <summary>Options for <see cref="MigrationTestRunner"/>.</summary>
public sealed class MigrationTestOptions
{
    /// <summary>
    /// Creates a new context configured with <c>UseSqlServer(connectionString)</c> and the migrations to test.
    /// Called once per migration; the runner points each context at the test database (see <see cref="DatabaseName"/>),
    /// so the connection string only needs the server and credentials.
    /// </summary>
    public required Func<DbContext> CreateContext { get; init; }

    /// <summary>
    /// Migrations <see cref="MigrationTestRunner.RunAllAsync"/> tests; null tests all of them.
    /// They run in EF apply order, whatever the order here.
    /// </summary>
    public IReadOnlyList<string>? MigrationIds { get; init; }

    /// <summary>
    /// The database the runner drops and recreates for every migration. Null (the default) uses a throwaway
    /// database named after the connection string's database plus a random suffix, dropped when the run ends.
    /// Only set this to a database you are happy to lose.
    /// </summary>
    public string? DatabaseName { get; init; }

    /// <summary>Leave the test database in place after the run, to inspect the last migration's data.</summary>
    public bool KeepDatabase { get; init; }

    /// <summary>
    /// Count a migration as failed when some of its scenarios could not be planted
    /// (<see cref="ScenarioStatus.NotSeeded"/>), not only when the migration itself fails.
    /// </summary>
    public bool RequireAllScenariosSeeded { get; init; }
}
