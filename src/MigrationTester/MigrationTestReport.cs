using MigrationTester.Runner;

namespace MigrationTester;

/// <summary>Results of <see cref="MigrationTestRunner.RunAllAsync"/> or <see cref="MigrationTestRunner.RunRangeAsync"/>.</summary>
public sealed class MigrationTestReport
{
    /// <summary>One result per migration, in EF apply order.</summary>
    public required IReadOnlyList<MigrationStepResult> Steps { get; init; }

    /// <summary>The database the run used: the generated throwaway name, or <see cref="MigrationTestOptions.DatabaseName"/>.</summary>
    public required string DatabaseName { get; init; }

    /// <summary>True when the database was left in place (<see cref="MigrationTestOptions.KeepDatabase"/>).</summary>
    public bool DatabaseKept { get; init; }

    /// <summary>Problems outside any one migration, such as failing to drop the throwaway database.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];

    /// <summary>True when every step <see cref="MigrationStepResult.Succeeded"/>.</summary>
    public bool Succeeded => Steps.All(static s => s.Succeeded);

    /// <summary>The steps that did not <see cref="MigrationStepResult.Succeeded"/>.</summary>
    public IReadOnlyList<MigrationStepResult> FailedSteps => [.. Steps.Where(static s => !s.Succeeded)];

    /// <summary>Throws <see cref="MigrationTestException"/> with the full report unless <see cref="Succeeded"/>.</summary>
    public void ThrowIfFailed()
    {
        if (!Succeeded)
        {
            throw new MigrationTestException(ToString(), FailedSteps);
        }
    }

    /// <summary>Readable report: a summary line, then every failed step in full and one line per passed step.</summary>
    public override string ToString() => ReportFormatter.Report(this);
}
