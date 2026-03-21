using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationRepair.Examples.Migrations
{
    /// <inheritdoc />
    public partial class _09_AddUniqueProductCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            //migrationBuilder.Sql("DELETE FROM [Products] WHERE [Code] IN (SELECT [Code] FROM (SELECT [Code], COUNT(*) AS [Count] FROM [Products] GROUP BY [Code]) AS [DuplicateCodes] WHERE [Count] > 1)");


            migrationBuilder.CreateIndex(
                name: "IX_Products_Code",
                table: "Products",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Products_Code",
                table: "Products");
        }
    }
}
