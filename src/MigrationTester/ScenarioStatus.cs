namespace MigrationTester;

/// <summary>Whether a scenario's data was in the database when the migration ran.</summary>
public enum ScenarioStatus
{
    /// <summary>The data was planted before the migration ran.</summary>
    Planted,

    /// <summary>
    /// The migration cannot fail this way, e.g. the table does not exist before it or the columns are
    /// already unique. Not a gap in coverage.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// The migration could fail this way, but the data could not be planted; see <see cref="MigrationScenario.Reason"/>.
    /// A gap in coverage, which <see cref="MigrationTestOptions.RequireAllScenariosSeeded"/> turns into a failure.
    /// </summary>
    NotSeeded
}
