using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MigrationTester.Examples.Migrations
{
    /// <summary>
    /// Changes Tags.ExternalId from nvarchar(100) to uniqueidentifier.
    /// Scenario: ColumnTypeConversionIncompatible — existing Tags rows may have ExternalId
    /// values that are not valid GUIDs.
    /// </summary>
    public partial class _07_ChangeTagExternalIdToGuid : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // FIX: uncomment to clean up non-GUID values before changing the column type.
            // migrationBuilder.Sql(
            //     @"UPDATE [Tags] SET [ExternalId] = NULL
            //       WHERE [ExternalId] IS NOT NULL
            //         AND TRY_CAST([ExternalId] AS uniqueidentifier) IS NULL;");

            migrationBuilder.AlterColumn<Guid>(
                name: "ExternalId",
                table: "Tags",
                type: "uniqueidentifier",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "ExternalId",
                table: "Tags",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uniqueidentifier",
                oldNullable: true);
        }
    }
}
