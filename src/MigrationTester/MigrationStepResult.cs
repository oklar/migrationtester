using MigrationTester.Runner;

namespace MigrationTester;

/// <summary>What happened when one migration ran on data planted at the migration before it.</summary>
public sealed class MigrationStepResult
{
    /// <summary>EF migration id (e.g. 20240101120000_MyMigration).</summary>
    public required string MigrationId { get; init; }

    public required MigrationStepOutcome Outcome { get; init; }

    /// <summary>The migration's error (<see cref="MigrationStepOutcome.Failed"/>) or the setup error (<see cref="MigrationStepOutcome.Error"/>).</summary>
    public Exception? Exception { get; init; }

    /// <summary>Every scenario derived from the migration's <c>Up()</c>, with its <see cref="MigrationScenario.Status"/>.</summary>
    public IReadOnlyList<MigrationScenario> Scenarios { get; init; } = [];

    /// <summary>
    /// Planted scenarios the failure matches, judged by SQL Server's error numbers and the names in its messages.
    /// Empty when the step passed or when the error matches none of them.
    /// </summary>
    public IReadOnlyList<MigrationScenario> FailingScenarios { get; init; } = [];

    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>Seed statements that ran before the migration, in order.</summary>
    public IReadOnlyList<string> SqlExecuted { get; init; } = [];

    /// <summary>True when <c>Up()</c> calls <c>migrationBuilder.Sql(...)</c>, whose effects are not analyzed.</summary>
    public bool ContainsRawSql { get; init; }

    /// <summary>The database the step ran in.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>Wall-clock time for this step (fresh database, seed, migrate).</summary>
    public TimeSpan Duration { get; init; }

    internal bool RequireAllScenariosSeeded { get; init; }

    /// <summary>What went wrong before the migration ran, for <see cref="MigrationStepOutcome.Error"/>.</summary>
    internal string? ErrorStage { get; init; }

    /// <summary>
    /// True when the migration passed and, with <see cref="MigrationTestOptions.RequireAllScenariosSeeded"/>,
    /// no scenario was <see cref="ScenarioStatus.NotSeeded"/>.
    /// </summary>
    public bool Succeeded =>
        Outcome == MigrationStepOutcome.Passed
        && !(RequireAllScenariosSeeded && Scenarios.Any(static s => s.Status == ScenarioStatus.NotSeeded));

    /// <summary>Readable account of the step: the error, the scenarios it broke on, repair hints and coverage gaps.</summary>
    public string Summary => ReportFormatter.Step(this);

    /// <summary>Throws <see cref="MigrationTestException"/> with <see cref="Summary"/> unless <see cref="Succeeded"/>.</summary>
    public void ThrowIfFailed()
    {
        if (!Succeeded)
        {
            throw new MigrationTestException(Summary, [this]);
        }
    }

    public override string ToString() => Summary;
}
