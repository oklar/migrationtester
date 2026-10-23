using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Schema;

namespace MigrationTester.Planning;

/// <summary>
/// Maps <see cref="MigrationOperation"/> instances to adversarial scenarios (deterministic).
/// Facts about the column before the migration come from the live N-1 schema when it has them,
/// otherwise from the operation's <c>OldColumn</c>.
/// </summary>
internal static class ScenarioPlanner
{
    private const string TableMissing = "Table does not exist at migration N-1.";

    public static IReadOnlyList<MigrationScenario> Plan(
        IReadOnlyList<MigrationOperation> operations,
        RelationalSchemaModel schema)
    {
        var list = new List<MigrationScenario>();

        foreach (var op in operations)
        {
            switch (op)
            {
                case CreateIndexOperation { IsUnique: true } idx:
                    list.Add(UniqueFromIndex(idx, schema));
                    break;
                case AddUniqueConstraintOperation uq:
                    list.Add(Duplicate(
                        $"Duplicate values for unique constraint '{uq.Name}' on '{uq.Table}'", uq, uq.Schema, uq.Table, uq.Columns, schema));
                    break;
                case AddForeignKeyOperation fk:
                    list.Add(ForeignKeyScenario(fk, schema));
                    break;
                case AlterColumnOperation alter:
                    list.AddRange(AlterColumnScenarios(alter, schema));
                    break;
                case AddCheckConstraintOperation chk:
                    list.Add(CheckScenario(chk, schema));
                    break;
                case AddColumnOperation addCol:
                    var addColScenario = AddColumnScenario(addCol, schema);
                    if (addColScenario is not null)
                    {
                        list.Add(addColScenario);
                    }
                    break;
                case AddPrimaryKeyOperation pk:
                    list.Add(Duplicate(
                        $"Duplicate values for new primary key '{pk.Name}' on '{pk.Table}'", pk, pk.Schema, pk.Table, pk.Columns, schema));
                    break;
            }
        }

        return list;
    }

    private static MigrationScenario UniqueFromIndex(CreateIndexOperation idx, RelationalSchemaModel schema)
    {
        string name = $"Duplicate values for unique index '{idx.Name}' on '{idx.Table}'";
        if (string.IsNullOrWhiteSpace(idx.Filter))
        {
            return Duplicate(name, idx, idx.Schema, idx.Table, idx.Columns, schema);
        }

        if (!NotNullFilter.TryParse(idx.Filter, out var filterColumns))
        {
            var unfiltered = Duplicate(name, idx, idx.Schema, idx.Table, idx.Columns, schema);
            return unfiltered.Status != ScenarioStatus.Planted
                ? unfiltered
                : unfiltered.NotSeeded($"Filter '{idx.Filter}' is not a plain IS NOT NULL filter; duplicate seeds may not satisfy it.");
        }

        // Duplicates only count where the filter holds, so the seeded copies get non-NULL filter columns.
        return Duplicate(name, idx, idx.Schema, idx.Table, idx.Columns, schema, notNullFiltered: true) with
        {
            NotNullFilterColumns = filterColumns
        };
    }

    private static MigrationScenario Duplicate(
        string name,
        MigrationOperation op,
        string? schemaName,
        string tableName,
        string[]? keyColumns,
        RelationalSchemaModel schema,
        bool notNullFiltered = false)
    {
        var table = schema.FindTable(schemaName, tableName);
        var columns = keyColumns?.ToList() ?? [];
        var scenario = new MigrationScenario
        {
            Kind = ScenarioKind.DuplicateUniqueIndexOrConstraint,
            Name = name,
            Schema = schema.Resolve(schemaName, tableName).Schema,
            Table = tableName,
            Columns = columns,
            SourceOperation = op
        };

        if (table is null)
        {
            return scenario.NotApplicable(TableMissing);
        }

        if (columns.Count == 0)
        {
            return scenario.NotSeeded("Key has no column list; cannot build duplicate seed.");
        }

        return AlreadyUnique(table, columns, notNullFiltered) is { } existing
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

    private static MigrationScenario ForeignKeyScenario(AddForeignKeyOperation fk, RelationalSchemaModel schema)
    {
        bool present = schema.FindTable(fk.Schema, fk.Table) is not null
            && schema.FindTable(fk.PrincipalSchema, fk.PrincipalTable) is not null;
        var scenario = new MigrationScenario
        {
            Kind = ScenarioKind.ForeignKeyOrphan,
            Name = $"Orphan rows in '{fk.Table}' referencing missing '{fk.PrincipalTable}' when FK is added",
            Schema = schema.Resolve(fk.Schema, fk.Table).Schema,
            Table = fk.Table,
            Columns = fk.Columns?.ToList() ?? [],
            PrincipalSchema = schema.Resolve(fk.PrincipalSchema, fk.PrincipalTable).Schema,
            PrincipalTable = fk.PrincipalTable,
            PrincipalColumns = fk.PrincipalColumns?.ToList() ?? [],
            SourceOperation = fk
        };
        return present
            ? scenario
            : scenario.NotApplicable("Dependent or principal table missing at N-1; cannot seed orphan FK row.");
    }

    private static IEnumerable<MigrationScenario> AlterColumnScenarios(
        AlterColumnOperation alter,
        RelationalSchemaModel schema)
    {
        var table = schema.FindTable(alter.Schema, alter.Table);
        var live = table?.FindColumn(alter.Name);
        var oldCol = alter.OldColumn;
        var baseScenario = new MigrationScenario
        {
            Kind = ScenarioKind.NonNullableColumnWithNull,
            Name = "",
            Schema = schema.Resolve(alter.Schema, alter.Table).Schema,
            Table = alter.Table,
            Columns = [alter.Name],
            SourceOperation = alter,
            Status = table is null ? ScenarioStatus.NotApplicable : ScenarioStatus.Planted,
            Reason = table is null ? TableMissing : null
        };

        bool wasNullable = live?.IsNullable ?? oldCol.IsNullable;
        if (wasNullable && !alter.IsNullable)
        {
            yield return baseScenario with
            {
                Name = $"NULL in '{alter.Name}' before column becomes NOT NULL"
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
                Name = $"String longer than new max length for '{alter.Name}'",
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
                Name = $"Value overflow when precision shrinks for '{alter.Name}'",
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
                Name = $"Existing data in '{alter.Name}' may not convert from {oldText} to {newText}",
                OldColumnType = oldText,
                NewColumnType = newText,
                SeedValue = failingValue
            };
        }
    }

    private static MigrationScenario CheckScenario(AddCheckConstraintOperation chk, RelationalSchemaModel schema)
    {
        bool present = schema.FindTable(chk.Schema, chk.Table) is not null;
        return new MigrationScenario
        {
            Kind = ScenarioKind.CheckConstraintViolation,
            Name = $"Rows violating new check constraint '{chk.Name}'",
            Schema = schema.Resolve(chk.Schema, chk.Table).Schema,
            Table = chk.Table,
            Columns = [],
            SourceOperation = chk,
            Status = present ? ScenarioStatus.NotSeeded : ScenarioStatus.NotApplicable,
            Reason = present
                ? "Automatic row generation from arbitrary CHECK SQL is not supported; add a custom integration test."
                : TableMissing
        };
    }

    private static MigrationScenario? AddColumnScenario(AddColumnOperation addCol, RelationalSchemaModel schema)
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

        bool present = schema.FindTable(addCol.Schema, addCol.Table) is not null;
        return new MigrationScenario
        {
            Kind = ScenarioKind.NotNullColumnAddedWithoutDefault,
            Name = $"NOT NULL column '{addCol.Name}' added to '{addCol.Table}' without a default value",
            Schema = schema.Resolve(addCol.Schema, addCol.Table).Schema,
            Table = addCol.Table,
            Columns = [addCol.Name],
            SourceOperation = addCol,
            Status = present ? ScenarioStatus.Planted : ScenarioStatus.NotApplicable,
            Reason = present ? null : TableMissing
        };
    }
}
