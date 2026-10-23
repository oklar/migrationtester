using MigrationTester.Planning;
using Xunit;

namespace MigrationTester.Tests;

public sealed class RepairHintCatalogTests
{
    [Fact]
    public void ForScenario_duplicate_unique_includes_deduplicate_keyword()
    {
        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint,
            Name = "test",
            Table = "Items",
            Columns = ["Code"]
        };

        string hint = RepairHintCatalog.ForScenario(scenario);

        Assert.Contains("Deduplicate", hint);
        Assert.Contains("[Code]", hint);
        Assert.Contains("[Items]", hint);
    }

    [Fact]
    public void ForScenario_foreign_key_orphan_includes_delete_pattern()
    {
        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.ForeignKeyOrphan,
            Name = "test",
            Table = "Orders",
            Columns = ["CustomerId"],
            PrincipalTable = "Customers",
            PrincipalColumns = ["Id"]
        };

        string hint = RepairHintCatalog.ForScenario(scenario);

        Assert.Contains("[Orders]", hint);
        Assert.Contains("[Customers]", hint);
        Assert.Contains("NOT EXISTS", hint);
    }

    [Fact]
    public void ForScenario_non_nullable_includes_update_where_null()
    {
        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.NonNullableColumnWithNull,
            Name = "test",
            Table = "Items",
            Columns = ["Name"]
        };

        string hint = RepairHintCatalog.ForScenario(scenario);

        Assert.Contains("NOT NULL", hint);
        Assert.Contains("IS NULL", hint);
        Assert.Contains("[Name]", hint);
    }

    [Fact]
    public void ForScenario_max_length_includes_truncation_update()
    {
        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.MaxLengthExceededAfterShrink,
            Name = "test",
            Table = "Items",
            Columns = ["Code"],
            NewMaxLength = 50
        };

        string hint = RepairHintCatalog.ForScenario(scenario);

        Assert.Contains("LEFT(", hint);
        Assert.Contains("50", hint);
    }

    [Fact]
    public void ForScenario_check_constraint_includes_predicate_guidance()
    {
        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.CheckConstraintViolation,
            Name = "test",
            Table = "Items"
        };

        string hint = RepairHintCatalog.ForScenario(scenario);

        Assert.Contains("CHECK", hint);
    }

    [Fact]
    public void ForScenario_not_null_add_column_includes_default_guidance()
    {
        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.NotNullColumnAddedWithoutDefault,
            Name = "test",
            Table = "Items",
            Columns = ["Status"]
        };

        string hint = RepairHintCatalog.ForScenario(scenario);

        Assert.Contains("NOT NULL", hint);
        Assert.Contains("[Status]", hint);
        Assert.Contains("DEFAULT", hint);
    }

    [Fact]
    public void ForScenario_type_conversion_includes_try_cast()
    {
        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.ColumnTypeConversionIncompatible,
            Name = "test",
            Table = "Items",
            Columns = ["Code"],
            OldColumnType = "nvarchar(100)",
            NewColumnType = "int"
        };

        string hint = RepairHintCatalog.ForScenario(scenario);

        Assert.Contains("TRY_CAST", hint);
        Assert.Contains("nvarchar(100)", hint);
        Assert.Contains("int", hint);
        Assert.Contains("[Code]", hint);
    }

    [Fact]
    public void ForScenario_precision_overflow_includes_power_and_round()
    {
        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.DecimalPrecisionOverflow,
            Name = "test",
            Table = "Items",
            Columns = ["Price"],
            NewPrecision = 10,
            NewScale = 2
        };

        string hint = RepairHintCatalog.ForScenario(scenario);

        Assert.Contains("POWER", hint);
        Assert.Contains("ROUND", hint);
        Assert.Contains("[Price]", hint);
    }
}
