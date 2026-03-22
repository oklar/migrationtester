using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationRepair.Examples.Migrations
{
    /// <summary>
    /// Makes Products.Name NOT NULL.
    /// Scenario: NonNullableColumnWithNull — existing Products rows may have NULL Name values.
    /// </summary>
    public partial class _05_MakeProductNameRequired : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: uncomment to backfill NULLs before making the column NOT NULL.
            // migrationBuilder.Sql(
            //     "UPDATE [Products] SET [Name] = N'Unknown' WHERE [Name] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200,
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }
    }
}
