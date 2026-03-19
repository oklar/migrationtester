using MigrationRepair.Planning;

namespace MigrationRepair.Runner;

public sealed class MigrationAdversaryResult
{
    public required bool MigrationSucceeded { get; init; }

    public string? FailureSummary { get; init; }

    public Exception? Exception { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = [];

    public IReadOnlyList<AdversaryScenario> PlannedScenarios { get; init; } = [];

    public IReadOnlyList<string> SqlExecuted { get; init; } = [];

    public bool ContainsRawSqlInMigration { get; init; }
}
