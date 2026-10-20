using MigrationRepair.Planning;

namespace MigrationRepair.Runner;

/// <summary>
/// Outcome of <see cref="MigrationAdversaryRunner.RunAsync"/>. Tests should treat
/// <see cref="MigrationSucceeded"/> == false as a failure (assert on
/// <see cref="FailureSummary"/> or <see cref="Exception"/>).
/// </summary>
public sealed class MigrationAdversaryResult
{
    /// <summary>True when the migration to N completed without error.</summary>
    public required bool MigrationSucceeded { get; init; }

    public string? FailureSummary { get; init; }

    public Exception? Exception { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<AdversaryScenario> PlannedScenarios { get; init; } = [];

    public IReadOnlyList<string> SqlExecuted { get; init; } = [];

    public bool ContainsRawSqlInMigration { get; init; }
}
