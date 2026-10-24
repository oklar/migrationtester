using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationTester.Examples.Migrations
{
    /// <summary>
    /// Shrinks Products.Code from nvarchar(200) to nvarchar(50).
    /// Scenario: MaxLengthExceededAfterShrink — existing Products rows may have Code values
    /// longer than 50 characters.
    /// </summary>
    public partial class _03_ShrinkProductCodeLength : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: uncomment to truncate long values before shrinking the column.
            // migrationBuilder.Sql(
            //     "UPDATE [Products] SET [Code] = LEFT([Code], 50) WHERE LEN([Code]) > 50;");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Products",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(200)",
                oldMaxLength: 200);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "Products",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(50)",
                oldMaxLength: 50);
        }
    }
}
