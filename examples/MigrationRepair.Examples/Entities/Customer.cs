namespace MigrationRepair.Examples.Entities;

public sealed class Customer
{
    public int Id { get; set; }

    public string Name { get; set; } = "";

    public ICollection<Order> Orders { get; set; } = new List<Order>();
}
