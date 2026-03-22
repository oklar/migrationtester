using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationRepair.Examples.Migrations
{
    /// <summary>
    /// Adds unique index on Products.Code.
    /// Scenario: DuplicateUniqueIndexOrConstraint — existing Products rows may have
    /// duplicate Code values.
    /// </summary>
    public partial class _04_AddUniqueProductCode : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: uncomment to deduplicate before creating the unique index.
            // migrationBuilder.Sql(
            //     @"WITH cte AS (
            //         SELECT [Id], ROW_NUMBER() OVER (PARTITION BY [Code] ORDER BY [Id]) AS rn
            //         FROM [Products])
            //     DELETE FROM cte WHERE rn > 1;");

            migrationBuilder.CreateIndex(
                name: "IX_Products_Code",
                table: "Products",
                column: "Code",
                unique: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Code",
                table: "Products");
        }
    }
}
