using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MigrationTester.Planning;

/// <summary>
/// A deterministic adversarial case derived from a single <see cref="MigrationOperation"/>.
/// </summary>
public sealed record AdversaryScenario
{
    public required AdversaryScenarioKind Kind { get; init; }

    /// <summary>User-facing label for assertions and reports.</summary>
    public required string Name { get; init; }

    /// <summary>Schema of <see cref="Table"/>; null when not resolved against a live database.</summary>
    public string? Schema { get; init; }

    /// <summary>Table targeted for seed SQL (unquoted logical name).</summary>
    public required string Table { get; init; }

    /// <summary>Columns involved (e.g. unique key columns, FK columns).</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Schema of <see cref="PrincipalTable"/> for FK scenarios.</summary>
    public string? PrincipalSchema { get; init; }

    /// <summary>Optional related principal table for FK scenarios.</summary>
    public string? PrincipalTable { get; init; }

    public IReadOnlyList<string>? PrincipalColumns { get; init; }

    /// <summary>For length reduction: old max length (nullable = max/unlimited for strings).</summary>
    public int? OldMaxLength { get; init; }

    /// <summary>For length reduction: new max length after migration.</summary>
    public int? NewMaxLength { get; init; }

    /// <summary>For type conversion: SQL type before the ALTER.</summary>
    public string? OldColumnType { get; init; }

    /// <summary>For type conversion: SQL type after the ALTER.</summary>
    public string? NewColumnType { get; init; }

    /// <summary>For precision reduction: old precision.</summary>
    public int? OldPrecision { get; init; }

    /// <summary>For precision reduction: new precision.</summary>
    public int? NewPrecision { get; init; }

    /// <summary>For precision reduction: old scale.</summary>
    public int? OldScale { get; init; }

    /// <summary>For precision reduction: new scale.</summary>
    public int? NewScale { get; init; }

    /// <summary>Original migration operation (for debugging).</summary>
    public MigrationOperation? SourceOperation { get; init; }

    /// <summary>
    /// True when no adversarial data was planted for this scenario: the planner ruled it out
    /// (e.g. the table does not exist at N-1) or seeding could not insert the data.
    /// </summary>
    public bool Skipped { get; init; }

    public string? SkipReason { get; init; }

    /// <summary>SQL literal valid for the column at N-1 that the migration's conversion rejects.</summary>
    internal string? SeedValue { get; init; }

    /// <summary>For filtered unique indexes: columns the filter requires to be non-NULL.</summary>
    internal IReadOnlyList<string> NotNullFilterColumns { get; init; } = [];
}
