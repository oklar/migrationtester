namespace MigrationTester;

/// <summary>Thrown by <c>ThrowIfFailed()</c>; the message is the readable report, so test runners show it in full.</summary>
public sealed class MigrationTestException : Exception
{
    internal MigrationTestException(string message, IReadOnlyList<MigrationStepResult> failedSteps)
        : base(message)
    {
        FailedSteps = failedSteps;
    }

    /// <summary>The steps that did not succeed.</summary>
    public IReadOnlyList<MigrationStepResult> FailedSteps { get; }
}
