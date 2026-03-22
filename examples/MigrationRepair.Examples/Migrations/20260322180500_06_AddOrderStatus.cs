using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationRepair.Examples.Migrations
{
    /// <summary>
    /// Adds Orders.Status as NOT NULL without a default value.
    /// Scenario: NotNullColumnAddedWithoutDefault — existing Orders rows cannot satisfy
    /// the NOT NULL constraint because there is no default value.
    /// </summary>
    public partial class _06_AddOrderStatus : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: uncomment to add the column as nullable first, backfill, then make NOT NULL.
            // migrationBuilder.AddColumn<string>(
            //     name: "Status",
            //     table: "Orders",
            //     type: "nvarchar(32)",
            //     maxLength: 32,
            //     nullable: true);
            // migrationBuilder.Sql("UPDATE [Orders] SET [Status] = N'Pending' WHERE [Status] IS NULL;");
            // migrationBuilder.AlterColumn<string>(
            //     name: "Status",
            //     table: "Orders",
            //     type: "nvarchar(32)",
            //     maxLength: 32,
            //     nullable: false,
            //     defaultValue: "",
            //     oldClrType: typeof(string),
            //     oldType: "nvarchar(32)",
            //     oldMaxLength: 32,
            //     oldNullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Status",
                table: "Orders",
                type: "nvarchar(32)",
                maxLength: 32,
                nullable: false);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Orders");
        }
    }
}
