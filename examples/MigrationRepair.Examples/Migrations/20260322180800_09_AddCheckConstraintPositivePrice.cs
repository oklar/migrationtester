using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationRepair.Examples.Migrations
{
    /// <summary>
    /// Adds CHECK constraint requiring Products.Price > 0.
    /// Scenario: CheckConstraintViolation — the planner detects this but always skips it
    /// because automatic row generation from arbitrary CHECK SQL is not supported.
    /// This migration succeeds because no adversarial data is seeded.
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
