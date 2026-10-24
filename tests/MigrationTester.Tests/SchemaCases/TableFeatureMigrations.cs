using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MigrationTester.Tests.SchemaCases;

// Tables whose columns SQL Server fills itself: seeding must leave them out and still plant duplicates.

public sealed class ComputedColumnContext(DbContextOptions<ComputedColumnContext> options) : DbContext(options);

[DbContext(typeof(ComputedColumnContext))]
[Migration("20260101000001_CreateInvoices")]
public sealed class CreateInvoices : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Invoices",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Number = table.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
                Net = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
                Vat = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false),
                Gross = table.Column<decimal>("decimal(19,2)", nullable: false, computedColumnSql: "[Net] + [Vat]", stored: true),
                Label = table.Column<string>("nvarchar(24)", nullable: false, computedColumnSql: "CONCAT(N'INV-', [Number])", stored: false)
            },
            constraints: table => table.PrimaryKey("PK_Invoices", x => x.Id));
}

[DbContext(typeof(ComputedColumnContext))]
[Migration("20260101000002_AddUniqueInvoiceNumber")]
public sealed class AddUniqueInvoiceNumber : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Invoices_Number", "Invoices", "Number", unique: true);
}

public sealed class TemporalTableContext(DbContextOptions<TemporalTableContext> options) : DbContext(options);

// A system-versioned table as SQL Server defines it; raw SQL keeps the test independent of EF's temporal annotations.
[DbContext(typeof(TemporalTableContext))]
[Migration("20260101000001_CreateTemporalContracts")]
public sealed class CreateTemporalContracts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("""
            CREATE TABLE [Contracts] (
                [Id] int IDENTITY(1, 1) NOT NULL CONSTRAINT [PK_Contracts] PRIMARY KEY,
                [Reference] nvarchar(50) NOT NULL,
                [ValidFrom] datetime2 GENERATED ALWAYS AS ROW START NOT NULL,
                [ValidTo] datetime2 GENERATED ALWAYS AS ROW END NOT NULL,
                PERIOD FOR SYSTEM_TIME ([ValidFrom], [ValidTo]))
            WITH (SYSTEM_VERSIONING = ON (HISTORY_TABLE = [dbo].[ContractsHistory]));
            """);
}

[DbContext(typeof(TemporalTableContext))]
[Migration("20260101000002_AddUniqueContractReference")]
public sealed class AddUniqueContractReference : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Contracts_Reference", "Contracts", "Reference", unique: true);
}
