namespace MigrationTester;

/// <summary>How one migration fared on the planted data.</summary>
public enum MigrationStepOutcome
{
    /// <summary>The migration ran successfully on the planted data.</summary>
    Passed,

    /// <summary>The migration threw while running on the planted data.</summary>
    Failed,

    /// <summary>
    /// The step could not be set up: reading the migration, migrating an empty database to the previous
    /// migration, or reading or seeding the database failed.
    /// </summary>
    Error
}
