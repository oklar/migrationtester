using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationRepair.Schema;

namespace MigrationRepair.Planning;

/// <summary>
/// Maps <see cref="MigrationOperation"/> instances to adversarial scenarios (deterministic).
/// Facts about the column before the migration come from the live N-1 schema when it has them,
/// otherwise from the operation's <c>OldColumn</c>.
/// </summary>
internal static class ScenarioPlanner
{
    private const string TableMissing = "Table does not exist at migration N-1.";

    public static IReadOnlyList<AdversaryScenario> Plan(
        IReadOnlyList<MigrationOperation> operations,
        RelationalSchemaModel schema)
    {
        var list = new List<AdversaryScenario>();

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

    private static AdversaryScenario UniqueFromIndex(CreateIndexOperation idx, RelationalSchemaModel schema)
    {
        var scenario = Duplicate(
            $"Duplicate values for unique index '{idx.Name}' on '{idx.Table}'", idx, idx.Schema, idx.Table, idx.Columns, schema);
        return scenario.Skipped || string.IsNullOrWhiteSpace(idx.Filter)
            ? scenario
            : scenario with
            {
                Skipped = true,
                SkipReason = "Filtered (partial) unique index; simple duplicate INSERT seeds may not violate the predicate."
            };
    }

    private static AdversaryScenario Duplicate(
        string name,
        MigrationOperation op,
        string? schemaName,
        string tableName,
        string[]? keyColumns,
        RelationalSchemaModel schema)
    {
        var table = schema.FindTable(schemaName, tableName);
        var columns = keyColumns?.ToList() ?? [];
        string? skipReason = table is null
            ? TableMissing
            : columns.Count == 0
                ? "Key has no column list; cannot build duplicate seed."
                : AlreadyUnique(table, columns) is { } existing
                    ? $"Existing unique key '{existing.Name}' already prevents duplicates in these columns at N-1."
                    : null;

        return new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint,
            Name = name,
            Schema = schema.Resolve(schemaName, tableName).Schema,
            Table = tableName,
            Columns = columns,
            SourceOperation = op,
            Skipped = skipReason is not null,
            SkipReason = skipReason
        };
    }

    /// <summary>An unfiltered unique key at N-1 whose columns are a subset of <paramref name="columns"/>.</summary>
    private static UniqueKeyMetadata? AlreadyUnique(TableMetadata table, IReadOnlyList<string> columns)
    {
        var set = new HashSet<string>(columns, StringComparer.OrdinalIgnoreCase);
        return table.UniqueKeys.FirstOrDefault(k => k.Filter is null && k.Columns.All(set.Contains));
    }

    private static AdversaryScenario ForeignKeyScenario(AddForeignKeyOperation fk, RelationalSchemaModel schema)
    {
        bool present = schema.FindTable(fk.Schema, fk.Table) is not null
            && schema.FindTable(fk.PrincipalSchema, fk.PrincipalTable) is not null;
        return new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.ForeignKeyOrphan,
            Name = $"Orphan rows in '{fk.Table}' referencing missing '{fk.PrincipalTable}' when FK is added",
            Schema = schema.Resolve(fk.Schema, fk.Table).Schema,
            Table = fk.Table,
            Columns = fk.Columns?.ToList() ?? [],
            PrincipalSchema = schema.Resolve(fk.PrincipalSchema, fk.PrincipalTable).Schema,
            PrincipalTable = fk.PrincipalTable,
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
        RelationalSchemaModel schema)
    {
        var table = schema.FindTable(alter.Schema, alter.Table);
        var live = table?.FindColumn(alter.Name);
        var oldCol = alter.OldColumn;
        var baseScenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.NonNullableColumnWithNull,
            Name = "",
            Schema = schema.Resolve(alter.Schema, alter.Table).Schema,
            Table = alter.Table,
            Columns = [alter.Name],
            SourceOperation = alter,
            Skipped = table is null,
            SkipReason = table is null ? TableMissing : null
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
                Kind = AdversaryScenarioKind.MaxLengthExceededAfterShrink,
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
                Kind = AdversaryScenarioKind.DecimalPrecisionOverflow,
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
                Kind = AdversaryScenarioKind.ColumnTypeConversionIncompatible,
                Name = $"Existing data in '{alter.Name}' may not convert from {oldText} to {newText}",
                OldColumnType = oldText,
                NewColumnType = newText,
                SeedValue = failingValue
            };
        }
    }

    private static AdversaryScenario CheckScenario(AddCheckConstraintOperation chk, RelationalSchemaModel schema)
    {
        bool present = schema.FindTable(chk.Schema, chk.Table) is not null;
        return new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.CheckConstraintViolation,
            Name = $"Rows violating new check constraint '{chk.Name}'",
            Schema = schema.Resolve(chk.Schema, chk.Table).Schema,
            Table = chk.Table,
            Columns = [],
            SourceOperation = chk,
            Skipped = true,
            SkipReason = present
                ? "Automatic row generation from arbitrary CHECK SQL is not supported; add a custom integration test."
                : TableMissing
        };
    }

    private static AdversaryScenario? AddColumnScenario(AddColumnOperation addCol, RelationalSchemaModel schema)
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
        return new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.NotNullColumnAddedWithoutDefault,
            Name = $"NOT NULL column '{addCol.Name}' added to '{addCol.Table}' without a default value",
            Schema = schema.Resolve(addCol.Schema, addCol.Table).Schema,
            Table = addCol.Table,
            Columns = [addCol.Name],
            SourceOperation = addCol,
            Skipped = !present,
            SkipReason = present ? null : TableMissing
        };
    }
}
