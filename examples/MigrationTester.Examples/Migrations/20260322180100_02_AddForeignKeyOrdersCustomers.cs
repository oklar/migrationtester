using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationTester.Examples.Migrations
{
    /// <summary>
    /// Adds FK from Orders.CustomerId → Customers.Id.
    /// Scenario: ForeignKeyOrphan — existing Orders rows may reference CustomerId values
    /// that do not exist in Customers.
    /// </summary>
    public partial class _02_AddForeignKeyOrdersCustomers : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: uncomment to clean up orphan rows before adding the FK.
            // migrationBuilder.Sql(
            //     "DELETE FROM [Orders] WHERE [CustomerId] NOT IN (SELECT [Id] FROM [Customers]);");

            migrationBuilder.AddForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders",
                column: "CustomerId",
                principalTable: "Customers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Orders_Customers_CustomerId",
                table: "Orders");
        }
    }
}
