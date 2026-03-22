using MigrationRepair.Planning;
using Xunit;

namespace MigrationRepair.Tests;

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
}
