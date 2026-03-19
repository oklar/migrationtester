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
}
