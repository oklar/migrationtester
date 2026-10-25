using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationTester.Examples.Migrations
{
    /// <summary>
    /// Adds CHECK constraint requiring Products.Price > 0.
    /// Scenario: CheckConstraintViolation — existing Products rows may have a Price of 0 or less,
    /// which the new constraint rejects.
    /// </summary>
    public partial class _09_AddCheckConstraintPositivePrice : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: uncomment to ensure no rows violate the constraint before adding it.
            // migrationBuilder.Sql(
            //     "UPDATE [Products] SET [Price] = 1 WHERE [Price] <= 0;");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Products_PositivePrice",
                table: "Products",
                sql: "[Price] > 0");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Products_PositivePrice",
                table: "Products");
        }
    }
}
