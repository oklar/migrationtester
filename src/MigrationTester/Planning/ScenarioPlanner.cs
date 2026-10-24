using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Schema;

namespace MigrationTester.Planning;

/// <summary>
/// Maps <see cref="MigrationOperation"/> instances to adversarial scenarios (deterministic).
/// Facts about the column before the migration come from the live N-1 schema when it has them,
/// otherwise from the operation's <c>OldColumn</c>. Names renamed earlier in the same <c>Up()</c> map back to
/// their N-1 names (see <see cref="RenameMap"/>), which the scenarios hold.
/// </summary>
internal static class ScenarioPlanner
{
    private const string TableMissing = "Table does not exist at migration N-1.";

    public static IReadOnlyList<MigrationScenario> Plan(
        IReadOnlyList<MigrationOperation> operations,
        RelationalSchemaModel schema)
    {
        var list = new List<MigrationScenario>();
        var renames = new RenameMap(schema);

        foreach (var op in operations)
        {
            switch (op)
            {
                case RenameTableOperation renameTable:
                    renames.Rename(renameTable);
                    break;
                case RenameColumnOperation renameColumn:
                    renames.Rename(renameColumn);
                    break;
                case CreateIndexOperation { IsUnique: true } idx:
                    list.Add(UniqueFromIndex(idx, renames));
                    break;
                case AddUniqueConstraintOperation uq:
                    list.Add(Duplicate(
                        $"Duplicate values for unique constraint '{uq.Name}' on '{uq.Table}'", uq, Target.Of(renames, uq.Schema, uq.Table, uq.Columns)));
                    break;
                case AddForeignKeyOperation fk:
                    list.Add(ForeignKeyScenario(fk, renames));
                    break;
                case AlterColumnOperation alter:
                    list.AddRange(AlterColumnScenarios(alter, renames));
                    break;
                case AddCheckConstraintOperation chk:
                    list.Add(CheckScenario(chk, renames));
                    break;
                case AddColumnOperation addCol:
                    var addColScenario = AddColumnScenario(addCol, renames);
                    if (addColScenario is not null)
                    {
                        list.Add(addColScenario);
                    }
                    break;
                case AddPrimaryKeyOperation pk:
                    list.Add(Duplicate(
                        $"Duplicate values for new primary key '{pk.Name}' on '{pk.Table}'", pk, Target.Of(renames, pk.Schema, pk.Table, pk.Columns)));
                    break;
            }
        }

        return list;
    }

    private static MigrationScenario UniqueFromIndex(CreateIndexOperation idx, RenameMap renames)
    {
        string name = $"Duplicate values for unique index '{idx.Name}' on '{idx.Table}'";
        if (string.IsNullOrWhiteSpace(idx.Filter))
        {
            return Duplicate(name, idx, Target.Of(renames, idx.Schema, idx.Table, idx.Columns));
        }

        if (!NotNullFilter.TryParse(idx.Filter, out var filterColumns))
        {
            var unfiltered = Duplicate(name, idx, Target.Of(renames, idx.Schema, idx.Table, idx.Columns));
            return unfiltered.Status != ScenarioStatus.Planted
                ? unfiltered
                : unfiltered.NotSeeded($"Filter '{idx.Filter}' is not a plain IS NOT NULL filter; duplicate seeds may not satisfy it.");
        }

        // Duplicates only count where the filter holds, so the seeded copies get non-NULL filter columns.
        // Key and filter columns map back to N-1 together; the key columns come first.
        string[] keyColumns = idx.Columns ?? [];
        var target = Target.Of(renames, idx.Schema, idx.Table, [.. keyColumns, .. filterColumns]);
        return Duplicate(name, idx, target with { Columns = target.Columns.Take(keyColumns.Length).ToList() }, notNullFiltered: true) with
        {
            NotNullFilterColumns = target.Columns.Skip(keyColumns.Length).ToList()
        };
    }

    private static MigrationScenario Duplicate(
        string name,
        MigrationOperation op,
        Target target,
        bool notNullFiltered = false)
    {
        var scenario = new MigrationScenario
        {
            Kind = ScenarioKind.DuplicateUniqueIndexOrConstraint,
            Name = name + Note(target),
            Schema = target.Name.Schema,
            Table = target.Name.Name,
            Columns = target.Columns,
            SourceOperation = op
        };

        if (target.Table is not { } table)
        {
            return scenario.NotApplicable(TableMissing);
        }

        if (target.Columns.Count == 0)
        {
            return scenario.NotSeeded("Key has no column list; cannot build duplicate seed.");
        }

        if (target.RenamedAwayReason is { } renamedAway)
        {
            return scenario.NotSeeded(renamedAway);
        }

        return AlreadyUnique(table, target.Columns, notNullFiltered) is { } existing
            ? scenario.NotApplicable($"Existing unique key '{existing.Name}' already prevents duplicates in these columns at N-1.")
            : scenario;
    }

    /// <summary>
    /// A unique key at N-1 whose columns are a subset of <paramref name="columns"/>. When the new index is itself
    /// IS NOT NULL filtered, an existing IS NOT NULL filtered key counts too (an index rename via drop and create).
    /// </summary>
    private static UniqueKeyMetadata? AlreadyUnique(TableMetadata table, IReadOnlyList<string> columns, bool notNullFiltered)
    {
        var set = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        return table.UniqueKeys.FirstOrDefault(k =>
            k.Columns.All(set.Contains)
            && (k.Filter is null || (notNullFiltered && NotNullFilter.TryParse(k.Filter, out _))));
    }

    private static MigrationScenario ForeignKeyScenario(AddForeignKeyOperation fk, RenameMap renames)
    {
        var dependent = Target.Of(renames, fk.Schema, fk.Table, fk.Columns);
        var principal = Target.Of(renames, fk.PrincipalSchema, fk.PrincipalTable, fk.PrincipalColumns);
        var scenario = new MigrationScenario
        {
            Kind = ScenarioKind.ForeignKeyOrphan,
            Name = $"Orphan rows in '{fk.Table}' referencing missing '{fk.PrincipalTable}' when FK is added{Note(dependent, principal)}",
            Schema = dependent.Name.Schema,
            Table = dependent.Name.Name,
            Columns = dependent.Columns,
            PrincipalSchema = principal.Name.Schema,
            PrincipalTable = principal.Name.Name,
            PrincipalColumns = principal.Columns,
            SourceOperation = fk
        };

        if (dependent.Table is null || principal.Table is null)
        {
            return scenario.NotApplicable("Dependent or principal table missing at N-1; cannot seed orphan FK row.");
        }

        return dependent.RenamedAwayReason is { } renamedAway ? scenario.NotSeeded(renamedAway) : scenario;
    }

    private static IEnumerable<MigrationScenario> AlterColumnScenarios(
        AlterColumnOperation alter,
        RenameMap renames)
    {
        var target = Target.Of(renames, alter.Schema, alter.Table, [alter.Name]);
        var table = target.Table;
        var live = target.RenamedAwayReason is null ? table?.FindColumn(target.Columns[0]) : null;
        var oldCol = alter.OldColumn;
        string note = Note(target);
        var baseScenario = new MigrationScenario
        {
            Kind = ScenarioKind.NonNullableColumnWithNull,
            Name = "",
            Schema = target.Name.Schema,
            Table = target.Name.Name,
            Columns = target.Columns,
            SourceOperation = alter,
            Status = table is null ? ScenarioStatus.NotApplicable
                : target.RenamedAwayReason is null ? ScenarioStatus.Planted
                : ScenarioStatus.NotSeeded,
            Reason = table is null ? TableMissing : target.RenamedAwayReason
        };

        bool wasNullable = live?.IsNullable ?? oldCol.IsNullable;
        if (wasNullable && !alter.IsNullable)
        {
            yield return baseScenario with
            {
                Name = $"NULL in '{alter.Name}' before column becomes NOT NULL{note}"
            };
        }

        var oldType = live?.Type
            ?? SqlType.Resolve(oldCol.ColumnType, oldCol.MaxLength, oldCol.Precision, oldCol.Scale);
        var newType = SqlType.Resolve(alter.ColumnType, alter.MaxLength, alter.Precision, alter.Scale);

        int? newMax = alter.MaxLength ?? newType?.Length;
        int? oldMax = live?.Type.Length ?? oldCol.MaxLength ?? oldType?.Length;
        bool oldIsSized = oldType is null || oldType.Value.IsString || oldType.Value.IsBinary;
        if (oldIsSized && newMax is int nm && nm > 0 && (oldMax is null or SqlType.Max || oldMax > nm))
        {
            yield return baseScenario with
            {
                Kind = ScenarioKind.MaxLengthExceededAfterShrink,
                Name = $"String longer than new max length for '{alter.Name}'{note}",
                OldMaxLength = oldMax == SqlType.Max ? null : oldMax,
                NewMaxLength = nm
            };
        }

        if (oldType is not { } from || newType is not { } to
            || TypeConversionRules.FailingValue(from, to) is not { } failingValue)
        {
            yield break;
        }

        if (from.IsExactDecimal && to.IsExactDecimal)
        {
            yield return baseScenario with
            {
                Kind = ScenarioKind.DecimalPrecisionOverflow,
                Name = $"Value overflow when precision shrinks for '{alter.Name}'{note}",
                OldPrecision = from.Precision,
                NewPrecision = to.Precision,
                OldScale = from.Scale,
                NewScale = to.Scale,
                SeedValue = failingValue
            };
        }
        else
        {
            string oldText = live is null ? oldCol.ColumnType ?? from.ToString() : from.ToString();
            string newText = alter.ColumnType ?? to.ToString();
            yield return baseScenario with
            {
                Kind = ScenarioKind.ColumnTypeConversionIncompatible,
                Name = $"Existing data in '{alter.Name}' may not convert from {oldText} to {newText}{note}",
                OldColumnType = oldText,
                NewColumnType = newText,
                SeedValue = failingValue
            };
        }
    }

    private static MigrationScenario CheckScenario(AddCheckConstraintOperation chk, RenameMap renames)
    {
        var target = Target.Of(renames, chk.Schema, chk.Table);
        bool present = target.Table is not null;
        return new MigrationScenario
        {
            Kind = ScenarioKind.CheckConstraintViolation,
            Name = $"Rows violating new check constraint '{chk.Name}'{Note(target)}",
            Schema = target.Name.Schema,
            Table = target.Name.Name,
            Columns = [],
            SourceOperation = chk,
            Status = present ? ScenarioStatus.NotSeeded : ScenarioStatus.NotApplicable,
            Reason = present
                ? "Automatic row generation from arbitrary CHECK SQL is not supported; add a custom integration test."
                : TableMissing
        };
    }

    private static MigrationScenario? AddColumnScenario(AddColumnOperation addCol, RenameMap renames)
    {
        // Identity, rowversion and computed columns fill existing rows themselves.
        if (addCol.IsNullable
            || addCol.DefaultValue is not null
            || addCol.DefaultValueSql is not null
            || addCol.ComputedColumnSql is not null
            || addCol.IsRowVersion
            || addCol["SqlServer:Identity"] is not null)
        {
            return null;
        }

        // The new column has no N-1 name; only its table may have been renamed.
        var target = Target.Of(renames, addCol.Schema, addCol.Table);
        bool present = target.Table is not null;
        return new MigrationScenario
        {
            Kind = ScenarioKind.NotNullColumnAddedWithoutDefault,
            Name = $"NOT NULL column '{addCol.Name}' added to '{addCol.Table}' without a default value{Note(target)}",
            Schema = target.Name.Schema,
            Table = target.Name.Name,
            Columns = [addCol.Name],
            SourceOperation = addCol,
            Status = present ? ScenarioStatus.Planted : ScenarioStatus.NotApplicable,
            Reason = present ? null : TableMissing
        };
    }

    /// <summary>" ('New' was 'Old' before this migration)" for the renames the targets followed; empty without any.</summary>
    private static string Note(params Target[] targets)
    {
        var renamed = targets.SelectMany(static t => t.Renamed).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        return renamed.Count == 0 ? "" : $" ({string.Join(", ", renamed)} before this migration)";
    }

    /// <summary>A table and columns as one operation names them, mapped back to N-1 through the renames before it.</summary>
    /// <param name="Name">The table's N-1 name; the operation's own when the table does not exist at N-1.</param>
    /// <param name="Table">The table at N-1, if it exists.</param>
    /// <param name="Columns">N-1 column names, in the operation's order; the operation's own for a column renamed away.</param>
    /// <param name="RenamedAwayReason">Why nothing can be planted when an operation names a column that an earlier rename moved away.</param>
    /// <param name="Renamed">"'New' was 'Old'" for each name that differs from N-1.</param>
    private sealed record Target(
        TableName Name,
        TableMetadata? Table,
        IReadOnlyList<string> Columns,
        string? RenamedAwayReason,
        IReadOnlyList<string> Renamed)
    {
        public static Target Of(RenameMap renames, string? schemaName, string table, IEnumerable<string>? columns = null)
        {
            var now = renames.Schema.Resolve(schemaName, table);
            var before = renames.TableAtN1(schemaName, table);
            var renamed = new List<string>();
            if (before is { } was && !was.Equals(now))
            {
                renamed.Add(string.Equals(was.Schema, now.Schema, StringComparison.OrdinalIgnoreCase)
                    ? $"'{now.Name}' was '{was.Name}'"
                    : $"'{now}' was '{was}'");
            }

            var mapped = new List<string>();
            string? renamedAway = null;
            foreach (string column in columns ?? [])
            {
                string? atN1 = before is { } n1Table ? renames.ColumnAtN1(n1Table, column) : column;
                if (atN1 is null)
                {
                    renamedAway ??= $"Column '{column}' does not exist at N-1: an earlier operation in this migration renamed the column of that name.";
                    mapped.Add(column);
                    continue;
                }

                if (!string.Equals(atN1, column, StringComparison.OrdinalIgnoreCase))
                {
                    renamed.Add($"'{column}' was '{atN1}'");
                }

                mapped.Add(atN1);
            }

            return new Target(
                before ?? now,
                before is { } atN1Table ? renames.Schema.FindTable(atN1Table) : null,
                mapped,
                renamedAway,
                renamed);
        }
    }
}
