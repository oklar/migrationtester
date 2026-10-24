using MigrationTester.Schema;
using MigrationTester.Seeding;
using Xunit;
using static MigrationTester.Tests.TestSchema;

namespace MigrationTester.Tests;

public sealed class TableInsertOrderTests
{
    [Fact]
    public void Topological_orders_principal_before_dependent()
    {
        var orders = Dependent("Orders", "CustomerId", "Customers", nullable: false);
        var customers = Table("Customers", Col("Id", "int", identity: true));

        var result = TableInsertOrder.Topological([orders, customers]);

        Assert.Equal(["Customers", "Orders"], result.Select(static t => t.Name.Name));
    }

    [Fact]
    public void Topological_ignores_nullable_foreign_keys()
    {
        var orders = Dependent("Orders", "CustomerId", "Customers", nullable: true);
        var customers = Table("Customers", Col("Id", "int", identity: true));

        var result = TableInsertOrder.Topological([orders, customers]);

        Assert.Equal(["Orders", "Customers"], result.Select(static t => t.Name.Name));
    }

    [Fact]
    public void Topological_self_referencing_fk_does_not_count_as_cycle()
    {
        var zebra = Table("Zebra", Col("Id", "int", identity: true));
        var alpha = new TableMetadata
        {
            Name = new TableName("dbo", "Alpha"),
            Columns = [Col("Id", "int", identity: true), Col("ParentId", "int"), Col("ZebraId", "int")],
            ForeignKeys = [ForeignKey("ParentId", "Alpha"), ForeignKey("ZebraId", "Zebra")]
        };

        var result = TableInsertOrder.Topological([alpha, zebra]);

        Assert.Equal(["Zebra", "Alpha"], result.Select(static t => t.Name.Name));
    }

    [Fact]
    public void Topological_required_cycle_is_appended_by_name()
    {
        var b = Dependent("B", "AId", "A", nullable: false);
        var a = Dependent("A", "BId", "B", nullable: false);
        var c = Table("C", Col("Id", "int", identity: true));

        var result = TableInsertOrder.Topological([b, a, c]);

        Assert.Equal(["C", "A", "B"], result.Select(static t => t.Name.Name));
    }

    private static TableMetadata Dependent(string name, string column, string principal, bool nullable) =>
        new()
        {
            Name = new TableName("dbo", name),
            Columns = [Col("Id", "int", identity: true), Col(column, "int", nullable: nullable)],
            ForeignKeys = [ForeignKey(column, principal)]
        };
}
