using Microsoft.EntityFrameworkCore;
using MigrationRepair.Hints;

namespace MigrationRepair.Runner;

public sealed class MigrationAdversaryOptions
{
    /// <summary>Factory for a context configured with the same provider/options as production migrations.</summary>
    public required Func<DbContext> CreateContext { get; init; }

    /// <summary>Full migration id (e.g. 20240101120000_MyMigration).</summary>
    public required string TargetMigrationId { get; init; }

    /// <summary>When <see cref="DatabaseFamily.Portable"/>, family is inferred from the active provider.</summary>
    public DatabaseFamily DatabaseFamily { get; init; } = DatabaseFamily.Portable;

    /// <summary>Provider key passed to MigrationBuilder (e.g. Microsoft.EntityFrameworkCore.Sqlite).</summary>
    public string? ActiveProvider { get; init; }

    public bool EnableFuzzing { get; init; }

    public int FuzzIterationCount { get; init; } = 8;

    public int FuzzSeed { get; init; } = 42;
}
