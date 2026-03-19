using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationRepair.Tests.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueCode : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_Items_Code",
                table: "Items",
                column: "Code",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Items_Code",
                table: "Items");
        }
    }
}
