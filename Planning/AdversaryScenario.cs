using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MigrationRepair.Planning;

/// <summary>
/// A deterministic adversarial case derived from a single <see cref="MigrationOperation"/>.
/// </summary>
public sealed class AdversaryScenario
{
    public required AdversaryScenarioKind Kind { get; init; }

    /// <summary>User-facing label for assertions and reports.</summary>
    public required string Name { get; init; }

    /// <summary>Table targeted for seed SQL (unquoted logical name).</summary>
    public required string Table { get; init; }

    /// <summary>Columns involved (e.g. unique key columns, FK columns).</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Optional related principal table for FK scenarios.</summary>
    public string? PrincipalTable { get; init; }

    public IReadOnlyList<string>? PrincipalColumns { get; init; }

    /// <summary>For length reduction: old max length (nullable = max/unlimited for strings).</summary>
    public int? OldMaxLength { get; init; }

    /// <summary>For length reduction: new max length after migration.</summary>
    public int? NewMaxLength { get; init; }

    /// <summary>Original migration operation (for debugging).</summary>
    public MigrationOperation? SourceOperation { get; init; }

    /// <summary>When the table does not exist at N-1, seeding is skipped.</summary>
    public bool Skipped { get; init; }

    public string? SkipReason { get; init; }
}
