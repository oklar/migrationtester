namespace MigrationRepair.Planning;

public enum AdversaryScenarioKind
{
    DuplicateUniqueIndexOrConstraint,
    ForeignKeyOrphan,
    NonNullableColumnWithNull,
    MaxLengthExceededAfterShrink,
    CheckConstraintViolation,
    UnsupportedOperation,
    SkippedTableNotPresentAtNMinus1
}
