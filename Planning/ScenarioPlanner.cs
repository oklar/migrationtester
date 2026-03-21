using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;

namespace MigrationRepair.Planning;

/// <summary>
/// Maps <see cref="MigrationOperation"/> instances to adversarial scenarios (deterministic).
/// </summary>
public static class ScenarioPlanner
{
    /// <summary>
    /// Tables that exist after migrations through N-1 (unqualified names; comparison is ordinal ignore case).
    /// </summary>
    public static IReadOnlyList<AdversaryScenario> Plan(
        IReadOnlyList<MigrationOperation> operations,
        IReadOnlySet<string> tablesPresentAtNMinus1,
        StringComparer? tableNameComparer = null)
    {
        var nameComparer = tableNameComparer ?? StringComparer.OrdinalIgnoreCase;
        var set = new HashSet<string>(tablesPresentAtNMinus1, nameComparer);
        var list = new List<AdversaryScenario>();

        foreach (var op in operations)
        {
            switch (op)
            {
                case CreateIndexOperation { IsUnique: true } idx:
                    list.Add(UniqueFromIndex(idx, set));
                    break;
                case AddUniqueConstraintOperation uq:
                    list.Add(UniqueFromConstraint(uq, set));
                    break;
                case AddForeignKeyOperation fk:
                    list.Add(ForeignKeyScenario(fk, set));
                    break;
                case AlterColumnOperation alter:
                    list.AddRange(AlterColumnScenarios(alter, set));
                    break;
                case AddCheckConstraintOperation chk:
                    list.Add(CheckScenario(chk, set));
                    break;
            }
        }

        return list;
    }

    private static AdversaryScenario UniqueFromIndex(
        CreateIndexOperation idx,
        HashSet<string> tables)
    {
        string table = idx.Table;
        bool present = tables.Contains(table);
        bool hasFilter = !string.IsNullOrWhiteSpace(idx.Filter);
        var columns = idx.Columns?.ToList() ?? [];
        bool noColumns = columns.Count == 0;
        bool skipped = !present || hasFilter || noColumns;
        string? skipReason = null;
        if (!present)
        {
            skipReason = "Table does not exist at migration N-1; cannot seed duplicates before this migration's Up().";
        }
        else if (hasFilter)
        {
            skipReason = "Filtered (partial) unique index; simple duplicate INSERT seeds may not violate the predicate.";
        }
        else if (noColumns)
        {
            skipReason = "Unique index has no column list; cannot build duplicate seed.";
        }

        return new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint,
            Name = $"Duplicate values for unique index '{idx.Name}' on '{table}'",
            Table = table,
            Columns = columns,
            SourceOperation = idx,
            Skipped = skipped,
            SkipReason = skipped ? skipReason : null
        };
    }

    private static AdversaryScenario UniqueFromConstraint(
        AddUniqueConstraintOperation uq,
        HashSet<string> tables)
    {
        string table = uq.Table;
        bool present = tables.Contains(table);
        var columns = uq.Columns?.ToList() ?? [];
        bool noColumns = columns.Count == 0;
        return new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint,
            Name = $"Duplicate values for unique constraint '{uq.Name}' on '{table}'",
            Table = table,
            Columns = columns,
            SourceOperation = uq,
            Skipped = !present || noColumns,
            SkipReason = !present
                ? "Table does not exist at migration N-1."
                : noColumns
                    ? "Unique constraint has no column list; cannot build duplicate seed."
                    : null
        };
    }

    private static AdversaryScenario ForeignKeyScenario(
        AddForeignKeyOperation fk,
        HashSet<string> tables)
    {
        string dep = fk.Table;
        string principal = fk.PrincipalTable;
        bool depPresent = tables.Contains(dep);
        bool principalPresent = tables.Contains(principal);
        bool present = depPresent && principalPresent;
        return new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.ForeignKeyOrphan,
            Name = $"Orphan rows in '{dep}' referencing missing '{principal}' when FK is added",
            Table = dep,
            Columns = fk.Columns?.ToList() ?? [],
            PrincipalTable = principal,
            PrincipalColumns = fk.PrincipalColumns?.ToList() ?? [],
            SourceOperation = fk,
            Skipped = !present,
            SkipReason = present
                ? null
                : "Dependent or principal table missing at N-1; cannot seed orphan FK row."
        };
    }

    private static IEnumerable<AdversaryScenario> AlterColumnScenarios(
        AlterColumnOperation alter,
        HashSet<string> tables)
    {
        string table = alter.Table;
        bool present = tables.Contains(table);
        var oldCol = alter.OldColumn;

        if (oldCol is { IsNullable: true } && alter.IsNullable == false)
        {
            yield return new AdversaryScenario
            {
                Kind = AdversaryScenarioKind.NonNullableColumnWithNull,
                Name = $"NULL in '{alter.Name}' before column becomes NOT NULL",
                Table = table,
                Columns = [alter.Name],
                SourceOperation = alter,
                Skipped = !present,
                SkipReason = present ? null : "Table does not exist at N-1."
            };
        }

        int? oldMax = oldCol?.MaxLength;
        int? newMax = alter.MaxLength;
        if (newMax is int nm && nm > 0 && (oldMax is null || oldMax > nm))
        {
            yield return new AdversaryScenario
            {
                Kind = AdversaryScenarioKind.MaxLengthExceededAfterShrink,
                Name = $"String longer than new max length for '{alter.Name}'",
                Table = table,
                Columns = [alter.Name],
                OldMaxLength = oldMax,
                NewMaxLength = nm,
                SourceOperation = alter,
                Skipped = !present,
                SkipReason = present ? null : "Table does not exist at N-1."
            };
        }
    }

    private static AdversaryScenario CheckScenario(
        AddCheckConstraintOperation chk,
        HashSet<string> tables)
    {
        string table = chk.Table;
        bool present = tables.Contains(table);
        return new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.CheckConstraintViolation,
            Name = $"Rows violating new check constraint '{chk.Name}'",
            Table = table,
            Columns = [],
            SourceOperation = chk,
            Skipped = !present,
            SkipReason = present
                ? "Automatic row generation from arbitrary CHECK SQL is not supported; add a custom integration test or plugin."
                : "Table does not exist at N-1."
        };
    }
}
