using MigrationRepair.Schema;
using MigrationRepair.Seeding;
using Xunit;

namespace MigrationRepair.Tests;

public sealed class TableInsertOrderTests
{
    [Fact]
    public void Topological_self_referencing_fk_does_not_trigger_cycle_fallback()
    {
        var tables = new List<string> { "Zebra", "Alpha" };
        var fks = new List<ForeignKeyEdge>
        {
            new()
            {
                DependentTable = "Alpha",
                PrincipalTable = "Alpha",
                DependentColumns = ["ParentId"],
                PrincipalColumns = ["Id"]
            },
            new()
            {
                DependentTable = "Alpha",
                PrincipalTable = "Zebra",
                DependentColumns = ["ZebraId"],
                PrincipalColumns = ["Id"]
            }
        };

        var result = TableInsertOrder.Topological(tables, fks);

        // Correct topological order: Zebra (principal) before Alpha (dependent).
        // Without the self-ref skip, the false cycle would fall back to alphabetical ["Alpha", "Zebra"].
        Assert.Equal(2, result.Count);
        Assert.Equal("Zebra", result[0]);
        Assert.Equal("Alpha", result[1]);
    }

    [Fact]
    public void Topological_orders_principal_before_dependent()
    {
        var tables = new List<string> { "Orders", "Customers" };
        var fks = new List<ForeignKeyEdge>
        {
            new()
            {
                DependentTable = "Orders",
                PrincipalTable = "Customers",
                DependentColumns = ["CustomerId"],
                PrincipalColumns = ["Id"]
            }
        };

        var result = TableInsertOrder.Topological(tables, fks);

        Assert.Equal(2, result.Count);
        Assert.Equal("Customers", result[0]);
        Assert.Equal("Orders", result[1]);
    }

    [Fact]
    public void Topological_cycle_falls_back_to_alphabetical()
    {
        var tables = new List<string> { "B", "A" };
        var fks = new List<ForeignKeyEdge>
        {
            new()
            {
                DependentTable = "A",
                PrincipalTable = "B",
                DependentColumns = ["BId"],
                PrincipalColumns = ["Id"]
            },
            new()
            {
                DependentTable = "B",
                PrincipalTable = "A",
                DependentColumns = ["AId"],
                PrincipalColumns = ["Id"]
            }
        };

        var result = TableInsertOrder.Topological(tables, fks);

        Assert.Equal(2, result.Count);
        Assert.Equal("A", result[0]);
        Assert.Equal("B", result[1]);
    }

    [Fact]
    public void Topological_no_fks_preserves_input_order()
    {
        var tables = new List<string> { "C", "A", "B" };
        List<ForeignKeyEdge> fks = [];

        var result = TableInsertOrder.Topological(tables, fks);

        Assert.Equal(3, result.Count);
    }
}
