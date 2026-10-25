using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Planning;

namespace MigrationTester;

/// <summary>
/// One piece of data a migration may fail on, derived from a single <see cref="MigrationOperation"/>,
/// and whether it was planted before the migration ran.
/// </summary>
public sealed record MigrationScenario
{
    /// <summary>What kind of data the migration may fail on.</summary>
    public required ScenarioKind Kind { get; init; }

    /// <summary>User-facing label for assertions and reports.</summary>
    public required string Name { get; init; }

    /// <summary>Schema of <see cref="Table"/>; null when not resolved against a live database.</summary>
    public string? Schema { get; init; }

    /// <summary>Table the data is planted in (unquoted name).</summary>
    public required string Table { get; init; }

    /// <summary>Columns involved (e.g. unique key columns, FK columns).</summary>
    public IReadOnlyList<string> Columns { get; init; } = [];

    /// <summary>Schema of <see cref="PrincipalTable"/> for FK scenarios.</summary>
    public string? PrincipalSchema { get; init; }

    /// <summary>Principal table for FK scenarios.</summary>
    public string? PrincipalTable { get; init; }

    /// <summary>Referenced principal columns for FK scenarios, in the order of <see cref="Columns"/>.</summary>
    public IReadOnlyList<string>? PrincipalColumns { get; init; }

    /// <summary>For length reduction: old max length (null = max/unlimited for strings).</summary>
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

    /// <summary>The migration operation the scenario comes from.</summary>
    public MigrationOperation? SourceOperation { get; init; }

    /// <summary>Whether the data was planted before the migration ran.</summary>
    public ScenarioStatus Status { get; init; } = ScenarioStatus.Planted;

    /// <summary>Why the data was not planted; null when <see cref="Status"/> is <see cref="ScenarioStatus.Planted"/>.</summary>
    public string? Reason { get; init; }

    /// <summary>T-SQL showing how a migration can fix data like this before the statement that fails on it.</summary>
    public string RepairHint => RepairHintCatalog.ForScenario(this);

    /// <summary>SQL literal valid for the column at N-1 that the migration's conversion or new CHECK constraint rejects.</summary>
    internal string? SeedValue { get; init; }

    /// <summary>For filtered unique indexes: columns the filter requires to be non-NULL.</summary>
    internal IReadOnlyList<string> NotNullFilterColumns { get; init; } = [];

    internal MigrationScenario NotApplicable(string reason) => this with { Status = ScenarioStatus.NotApplicable, Reason = reason };

    internal MigrationScenario NotSeeded(string reason) => this with { Status = ScenarioStatus.NotSeeded, Reason = reason };
}
