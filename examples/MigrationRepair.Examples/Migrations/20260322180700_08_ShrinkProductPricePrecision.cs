using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationRepair.Examples.Migrations
{
    /// <summary>
    /// Shrinks Products.Price from decimal(18,6) to decimal(10,2).
    /// Scenario: DecimalPrecisionOverflow — existing Products rows may have Price values
    /// that overflow the smaller precision (e.g. values ≥ 100,000,000).
    /// </summary>
    public partial class _08_ShrinkProductPricePrecision : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: uncomment to round and cap values before shrinking precision.
            // migrationBuilder.Sql(
            //     @"UPDATE [Products] SET [Price] = ROUND([Price], 2)
            //       WHERE [Price] IS NOT NULL;
            //       UPDATE [Products] SET [Price] = 99999999.99
            //       WHERE [Price] >= 100000000;");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "Products",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(18,6)",
                oldPrecision: 18,
                oldScale: 6);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "Products",
                type: "decimal(18,6)",
                precision: 18,
                scale: 6,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "decimal(10,2)",
                oldPrecision: 10,
                oldScale: 2);
        }
    }
}
