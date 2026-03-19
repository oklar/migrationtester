namespace MigrationRepair.Runner;

/// <summary>Outcome of stressing a single migration inside <see cref="MigrationAdversaryRunner.RunAllAsync"/>.</summary>
public sealed class MigrationStepResult
{
    /// <summary>EF migration id (e.g. 20240101120000_MyMigration).</summary>
    public required string MigrationId { get; init; }

    /// <summary>Detailed result for this step (same shape as <see cref="MigrationAdversaryRunner.RunAsync"/>).</summary>
    public required MigrationAdversaryResult Result { get; init; }

    /// <summary>Wall-clock time for this step (fresh database, seed, migrate).</summary>
    public TimeSpan Duration { get; init; }
}
