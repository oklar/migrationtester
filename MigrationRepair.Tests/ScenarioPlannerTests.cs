using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationRepair.Planning;
using Xunit;

namespace MigrationRepair.Tests;

public sealed class ScenarioPlannerTests
{
    [Fact]
    public void Plan_unique_index_produces_duplicate_scenario_when_table_exists()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation
            {
                Name = "IX_Items_Code",
                Table = "Items",
                Columns = ["Code"],
                IsUnique = true
            }
        ];

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Items" };
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint, s.Kind);
        Assert.False(s.Skipped);
    }

    [Fact]
    public void Plan_unique_index_skipped_when_table_missing_at_N_minus_1()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation
            {
                Name = "IX_Items_Code",
                Table = "Items",
                Columns = ["Code"],
                IsUnique = true
            }
        ];

        var scenarios = ScenarioPlanner.Plan(ops, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        var s = Assert.Single(scenarios);
        Assert.True(s.Skipped);
    }

    [Fact]
    public void Plan_foreign_key_produces_orphan_scenario()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddForeignKeyOperation
            {
                Name = "FK_Orders_Customers",
                Table = "Orders",
                Columns = ["CustomerId"],
                PrincipalTable = "Customers",
                PrincipalColumns = ["Id"]
            }
        ];

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Orders", "Customers" };
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.ForeignKeyOrphan, s.Kind);
        Assert.False(s.Skipped);
        Assert.Equal("Orders", s.Table);
        Assert.Equal("Customers", s.PrincipalTable);
    }

    [Fact]
    public void Plan_foreign_key_skipped_when_dependent_table_missing()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddForeignKeyOperation
            {
                Name = "FK_Orders_Customers",
                Table = "Orders",
                Columns = ["CustomerId"],
                PrincipalTable = "Customers",
                PrincipalColumns = ["Id"]
            }
        ];

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Customers" };
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        var s = Assert.Single(scenarios);
        Assert.True(s.Skipped);
    }

    [Fact]
    public void Plan_alter_column_nullable_to_required_produces_scenario()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Description",
            Table = "Items",
            IsNullable = false
        };
        alter.OldColumn.IsNullable = true;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Items" };
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.NonNullableColumnWithNull, s.Kind);
        Assert.False(s.Skipped);
        Assert.Equal("Description", Assert.Single(s.Columns));
    }

    [Fact]
    public void Plan_alter_column_shrink_max_length_produces_scenario()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Code",
            Table = "Items",
            MaxLength = 50
        };
        alter.OldColumn.MaxLength = 200;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Items" };
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.MaxLengthExceededAfterShrink, s.Kind);
        Assert.Equal(50, s.NewMaxLength);
        Assert.Equal(200, s.OldMaxLength);
    }

    [Fact]
    public void Plan_alter_column_nullable_to_required_and_shrink_produces_two_scenarios()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Code",
            Table = "Items",
            IsNullable = false,
            MaxLength = 50
        };
        alter.OldColumn.IsNullable = true;
        alter.OldColumn.MaxLength = 200;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Items" };
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        Assert.Equal(2, scenarios.Count);
        Assert.Contains(scenarios, s => s.Kind == AdversaryScenarioKind.NonNullableColumnWithNull);
        Assert.Contains(scenarios, s => s.Kind == AdversaryScenarioKind.MaxLengthExceededAfterShrink);
    }

    [Fact]
    public void Plan_check_constraint_always_skipped()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddCheckConstraintOperation
            {
                Name = "CK_Items_PositiveQty",
                Table = "Items",
                Sql = "[Qty] > 0"
            }
        ];

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Items" };
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.CheckConstraintViolation, s.Kind);
        Assert.True(s.Skipped);
        Assert.NotNull(s.SkipReason);
    }

    [Fact]
    public void Plan_non_unique_index_is_ignored()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation
            {
                Name = "IX_Items_Name",
                Table = "Items",
                Columns = ["Name"],
                IsUnique = false
            }
        ];

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Items" };
        var scenarios = ScenarioPlanner.Plan(ops, tables);

        Assert.Empty(scenarios);
    }
}
