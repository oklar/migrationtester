namespace MigrationRepair.Examples.Entities;

public sealed class Product
{
    public int Id { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Note { get; set; }

    public ICollection<Tag> Tags { get; set; } = new List<Tag>();

    public ICollection<OrderLine> OrderLines { get; set; } = new List<OrderLine>();
}
