namespace MigrationRepair.Examples.Entities;

public sealed class Tag
{
    public int Id { get; set; }

    public int ProductId { get; set; }

    public Product Product { get; set; } = null!;

    public string Label { get; set; } = "";
}
