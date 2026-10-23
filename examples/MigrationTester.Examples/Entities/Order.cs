namespace MigrationTester.Examples.Entities;

public sealed class Order
{
    public int Id { get; set; }

    public int CustomerId { get; set; }

    public Customer Customer { get; set; } = null!;

    public string Status { get; set; } = "";

    public ICollection<OrderLine> Lines { get; set; } = new List<OrderLine>();
}
