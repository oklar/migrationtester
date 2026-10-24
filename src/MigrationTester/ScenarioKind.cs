namespace MigrationTester;

/// <summary>The kind of data a migration step may fail on.</summary>
public enum ScenarioKind
{
    /// <summary>Duplicate rows before a unique index, unique constraint or primary key is added.</summary>
    DuplicateUniqueIndexOrConstraint,

    /// <summary>A row referencing a missing principal row before a foreign key is added.</summary>
    ForeignKeyOrphan,

    /// <summary>A NULL before a column becomes NOT NULL.</summary>
    NonNullableColumnWithNull,

    /// <summary>A value longer than the column's new maximum length.</summary>
    MaxLengthExceededAfterShrink,

    /// <summary>
    /// A value breaking a new CHECK constraint, e.g. <c>0</c> for <c>[Price] &gt; 0</c>. Planted only when the constraint
    /// compares one column with literals; other constraints are reported as <see cref="ScenarioStatus.NotSeeded"/>.
    /// </summary>
    CheckConstraintViolation,

    /// <summary>Existing rows when a NOT NULL column without a default is added.</summary>
    NotNullColumnAddedWithoutDefault,

    /// <summary>A value the column's new type cannot hold, e.g. text in a column becoming <c>int</c>, or 32768 in one becoming <c>smallint</c>.</summary>
    ColumnTypeConversionIncompatible,

    /// <summary>A value with more integer digits than a changed <c>decimal</c> precision or scale allows.</summary>
    DecimalPrecisionOverflow
}
