using Microsoft.EntityFrameworkCore;

namespace MigrationTester.Runner;

/// <summary>Options for <see cref="MigrationAdversaryRunner.RunAllAsync"/> / <see cref="MigrationAdversaryRunner.RunRangeAsync"/>.</summary>
public sealed class MigrationAdversaryBatchOptions
{
    /// <summary>Factory for a context configured with SQL Server (same as production migrations).</summary>
    public required Func<DbContext> CreateContext { get; init; }

    /// <summary>
    /// When null, all migrations returned by <c>Database.GetMigrations()</c> run in EF apply order.
    /// When set, only these ids run (each must exist); order follows the assembly order, not this list’s order.
    /// </summary>
    public IReadOnlyList<string>? MigrationIds { get; init; }

    /// <summary>Override when capture must match a non-standard MigrationBuilder provider key.</summary>
    public string? ActiveProvider { get; init; }
}
