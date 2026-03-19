using Microsoft.EntityFrameworkCore;

namespace MigrationRepair.Runner;

public sealed class MigrationAdversaryOptions
{
    /// <summary>Factory for a context configured with SQL Server (same as production migrations).</summary>
    public required Func<DbContext> CreateContext { get; init; }

    /// <summary>Full migration id (e.g. 20240101120000_MyMigration).</summary>
    public required string TargetMigrationId { get; init; }

    /// <summary>Override when capture must match a non-standard MigrationBuilder provider key.</summary>
    public string? ActiveProvider { get; init; }

    public bool EnableFuzzing { get; init; }

    public int FuzzIterationCount { get; init; } = 8;

    public int FuzzSeed { get; init; } = 42;
}
