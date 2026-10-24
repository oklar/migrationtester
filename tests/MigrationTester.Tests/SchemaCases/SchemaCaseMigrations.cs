using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace MigrationTester.Tests.SchemaCases;

// Hand-written migration chains, one context per case so each case seeds its own small schema.
// No designer files or snapshots: the runner only needs Up().

public sealed class RowVersionContext(DbContextOptions<RowVersionContext> options) : DbContext(options);

[DbContext(typeof(RowVersionContext))]
[Migration("20260101000001_CreateDocuments")]
public sealed class CreateDocuments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Documents",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Title = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false),
                RowVersion = table.Column<byte[]>("rowversion", rowVersion: true, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Documents", x => x.Id));
}

[DbContext(typeof(RowVersionContext))]
[Migration("20260101000002_AddUniqueDocumentTitle")]
public sealed class AddUniqueDocumentTitle : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Documents_Title", "Documents", "Title", unique: true);
}

public sealed class GuidKeyContext(DbContextOptions<GuidKeyContext> options) : DbContext(options);

[DbContext(typeof(GuidKeyContext))]
[Migration("20260101000001_CreateAccounts")]
public sealed class CreateAccounts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Accounts",
            columns: table => new
            {
                Id = table.Column<Guid>("uniqueidentifier", nullable: false),
                Email = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Accounts", x => x.Id));
}

[DbContext(typeof(GuidKeyContext))]
[Migration("20260101000002_AddUniqueAccountEmail")]
public sealed class AddUniqueAccountEmail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Accounts_Email", "Accounts", "Email", unique: true);
}

public sealed class ShrinkLengthContext(DbContextOptions<ShrinkLengthContext> options) : DbContext(options);

[DbContext(typeof(ShrinkLengthContext))]
[Migration("20260101000001_CreateProducts")]
public sealed class CreateProducts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Products",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Code = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Products", x => x.Id));
}

[DbContext(typeof(ShrinkLengthContext))]
[Migration("20260101000002_ShrinkProductCode")]
public sealed class ShrinkProductCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(
            name: "Code",
            table: "Products",
            type: "nvarchar(60)",
            maxLength: 60,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100);
}

public sealed class CustomSchemaContext(DbContextOptions<CustomSchemaContext> options) : DbContext(options);

[DbContext(typeof(CustomSchemaContext))]
[Migration("20260101000001_CreateAppItems")]
public sealed class CreateAppItems : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("app");
        migrationBuilder.CreateTable(
            name: "Items",
            schema: "app",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Code = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Items", x => x.Id));
    }
}

[DbContext(typeof(CustomSchemaContext))]
[Migration("20260101000002_AddUniqueAppItemCode")]
public sealed class AddUniqueAppItemCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Items_Code", "Items", "Code", schema: "app", unique: true);
}

public sealed class RequiredForeignKeyContext(DbContextOptions<RequiredForeignKeyContext> options) : DbContext(options);

[DbContext(typeof(RequiredForeignKeyContext))]
[Migration("20260101000001_CreateCustomersAndOrders")]
public sealed class CreateCustomersAndOrders : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Customers",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Customers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Orders",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                CustomerId = table.Column<int>("int", nullable: false),
                Note = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Orders", x => x.Id);
                table.ForeignKey("FK_Orders_Customers_CustomerId", x => x.CustomerId, "Customers", "Id");
            });
    }
}

[DbContext(typeof(RequiredForeignKeyContext))]
[Migration("20260101000002_MakeOrderNoteRequired")]
public sealed class MakeOrderNoteRequired : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(
            name: "Note",
            table: "Orders",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100,
            oldNullable: true);
}

public sealed class NarrowingContext(DbContextOptions<NarrowingContext> options) : DbContext(options);

[DbContext(typeof(NarrowingContext))]
[Migration("20260101000001_CreateMeasurements")]
public sealed class CreateMeasurements : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Measurements",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Quantity = table.Column<int>("int", nullable: false),
                Amount = table.Column<decimal>("decimal(18,2)", precision: 18, scale: 2, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Measurements", x => x.Id));
}

[DbContext(typeof(NarrowingContext))]
[Migration("20260101000002_NarrowQuantityToSmallint")]
public sealed class NarrowQuantityToSmallint : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<short>(
            name: "Quantity",
            table: "Measurements",
            type: "smallint",
            nullable: false,
            oldClrType: typeof(int),
            oldType: "int");
}

[DbContext(typeof(NarrowingContext))]
[Migration("20260101000003_IncreaseAmountScale")]
public sealed class IncreaseAmountScale : Migration
{
    // Same precision, more decimals: integer digits drop from 16 to 14.
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<decimal>(
            name: "Amount",
            table: "Measurements",
            type: "decimal(18,4)",
            precision: 18,
            scale: 4,
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "decimal(18,2)",
            oldPrecision: 18,
            oldScale: 2);
}

public sealed class RequiredAndShorterContext(DbContextOptions<RequiredAndShorterContext> options) : DbContext(options);

[DbContext(typeof(RequiredAndShorterContext))]
[Migration("20260101000001_CreatePeople")]
public sealed class CreatePeople : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "People",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_People", x => x.Id));
}

[DbContext(typeof(RequiredAndShorterContext))]
[Migration("20260101000002_MakeNameRequiredAndShorter")]
public sealed class MakeNameRequiredAndShorter : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AlterColumn<string>(
            name: "Name",
            table: "People",
            type: "nvarchar(20)",
            maxLength: 20,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100,
            oldNullable: true);
}

public sealed class CheckedStatusContext(DbContextOptions<CheckedStatusContext> options) : DbContext(options);

[DbContext(typeof(CheckedStatusContext))]
[Migration("20260101000001_CreateTickets")]
public sealed class CreateTickets : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Tickets",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Status = table.Column<string>("nvarchar(20)", maxLength: 20, nullable: false),
                Code = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Tickets", x => x.Id);
                table.CheckConstraint("CK_Tickets_Status", "[Status] IN (N'Open', N'Closed')");
            });
}

public sealed class CheckedValuesContext(DbContextOptions<CheckedValuesContext> options) : DbContext(options);

[DbContext(typeof(CheckedValuesContext))]
[Migration("20260101000001_CreateSellersAndListings")]
public sealed class CreateSellersAndListings : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Sellers",
            columns: table => new { Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1") },
            constraints: table => table.PrimaryKey("PK_Sellers", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Listings",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Price = table.Column<decimal>("decimal(10,2)", precision: 10, scale: 2, nullable: false),
                Quantity = table.Column<int>("int", nullable: false),
                Status = table.Column<string>("nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Open"),
                Code = table.Column<string>("nvarchar(10)", maxLength: 10, nullable: false),
                SellerId = table.Column<int>("int", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Listings", x => x.Id));
    }
}

// What EF scaffolds for HasCheckConstraint: the predicate exactly as written.
[DbContext(typeof(CheckedValuesContext))]
[Migration("20260101000002_AddPositiveListingPrice")]
public sealed class AddPositiveListingPrice : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddCheckConstraint("CK_Listings_PositivePrice", "Listings", "[Price] > 0");
}

[DbContext(typeof(CheckedValuesContext))]
[Migration("20260101000003_AddListingQuantityRange")]
public sealed class AddListingQuantityRange : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddCheckConstraint("CK_Listings_QuantityRange", "Listings", "[Quantity] >= 1 AND [Quantity] <= 100");
}

[DbContext(typeof(CheckedValuesContext))]
[Migration("20260101000004_AddListingStatusValues")]
public sealed class AddListingStatusValues : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddCheckConstraint("CK_Listings_Status", "Listings", "[Status] IN (N'Open', N'Closed')");
}

// The CHECK is not recognized, so nothing is planted for it, but the ordinary row already breaks it ('v1' is not
// 3 long); the failure must not be blamed on the orphan row planted for the foreign key.
[DbContext(typeof(CheckedValuesContext))]
[Migration("20260101000005_AddListingCodeLengthAndSeller")]
public sealed class AddListingCodeLengthAndSeller : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint("CK_Listings_CodeLength", "Listings", "LEN([Code]) = 3");
        migrationBuilder.AddForeignKey("FK_Listings_Sellers_SellerId", "Listings", "SellerId", "Sellers", principalColumn: "Id");
    }
}

public sealed class FilteredUniqueContext(DbContextOptions<FilteredUniqueContext> options) : DbContext(options);

[DbContext(typeof(FilteredUniqueContext))]
[Migration("20260101000001_CreateProfilesAndUsers")]
public sealed class CreateProfilesAndUsers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Profiles",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Bio = table.Column<string>("nvarchar(max)", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Profiles", x => x.Id));

        migrationBuilder.CreateTable(
            name: "Users",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Email = table.Column<string>("nvarchar(200)", maxLength: 200, nullable: true),
                ProfileId = table.Column<int>("int", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Users", x => x.Id);
                table.ForeignKey("FK_Users_Profiles_ProfileId", x => x.ProfileId, "Profiles", "Id");
            });
    }
}

// What EF scaffolds for HasIndex(u => u.Email).IsUnique() on a nullable column.
[DbContext(typeof(FilteredUniqueContext))]
[Migration("20260101000002_AddUniqueUserEmail")]
public sealed class AddUniqueUserEmail : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Users_Email", "Users", "Email", unique: true, filter: "[Email] IS NOT NULL");
}

// What EF scaffolds for an optional one-to-one relationship.
[DbContext(typeof(FilteredUniqueContext))]
[Migration("20260101000003_AddUniqueUserProfile")]
public sealed class AddUniqueUserProfile : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Users_ProfileId", "Users", "ProfileId", unique: true, filter: "[ProfileId] IS NOT NULL");
}

public sealed class SeedDataContext(DbContextOptions<SeedDataContext> options) : DbContext(options);

// HasData-style lookup rows. Raw SQL stands in for InsertData, which needs a model to know Statuses.Id is an identity.
[DbContext(typeof(SeedDataContext))]
[Migration("20260101000001_CreateLookupsWithSeedRows")]
public sealed class CreateLookupsWithSeedRows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Statuses",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Statuses", x => x.Id));
        migrationBuilder.CreateTable(
            name: "Countries",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false),
                Name = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_Countries", x => x.Id));
        migrationBuilder.CreateTable(
            name: "Shipments",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                StatusId = table.Column<int>("int", nullable: false),
                CountryId = table.Column<int>("int", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Shipments", x => x.Id);
                table.ForeignKey("FK_Shipments_Statuses_StatusId", x => x.StatusId, "Statuses", "Id");
                table.ForeignKey("FK_Shipments_Countries_CountryId", x => x.CountryId, "Countries", "Id");
            });
        migrationBuilder.Sql(
            "SET IDENTITY_INSERT [Statuses] ON; INSERT INTO [Statuses] ([Id], [Name]) VALUES (1, N'Open'), (2, N'Closed'); SET IDENTITY_INSERT [Statuses] OFF;");
        migrationBuilder.Sql("INSERT INTO [Countries] ([Id], [Name]) VALUES (1, N'Norway');");
    }
}

[DbContext(typeof(SeedDataContext))]
[Migration("20260101000002_AddMoreSeedRows")]
public sealed class AddMoreSeedRows : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(
            "SET IDENTITY_INSERT [Statuses] ON; INSERT INTO [Statuses] ([Id], [Name]) VALUES (3, N'Archived'); SET IDENTITY_INSERT [Statuses] OFF;");
        migrationBuilder.Sql("INSERT INTO [Countries] ([Id], [Name]) VALUES (2, N'Sweden');");
    }
}

public sealed class OwnTransactionContext(DbContextOptions<OwnTransactionContext> options) : DbContext(options);

[DbContext(typeof(OwnTransactionContext))]
[Migration("20260101000001_CreateContacts")]
public sealed class CreateContacts : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Contacts",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Phone = table.Column<string>("nvarchar(30)", maxLength: 30, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Contacts", x => x.Id));
}

// A statement EF must run outside a transaction forces the plant-for-real path for this step.
[DbContext(typeof(OwnTransactionContext))]
[Migration("20260101000002_MakePhoneRequiredOutsideTransaction")]
public sealed class MakePhoneRequiredOutsideTransaction : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("SELECT 1;", suppressTransaction: true);
        migrationBuilder.AlterColumn<string>(
            name: "Phone",
            table: "Contacts",
            type: "nvarchar(30)",
            maxLength: 30,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(30)",
            oldMaxLength: 30,
            oldNullable: true);
    }
}

// Needs a clean database after the step above left planted data behind.
[DbContext(typeof(OwnTransactionContext))]
[Migration("20260101000003_AddUniquePhone")]
public sealed class AddUniquePhone : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Contacts_Phone", "Contacts", "Phone", unique: true);
}

public sealed class BrokenFirstContext(DbContextOptions<BrokenFirstContext> options) : DbContext(options);

[DbContext(typeof(BrokenFirstContext))]
[Migration("20260101000001_AlwaysFails")]
public sealed class AlwaysFails : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.Sql("THROW 50000, 'This migration fails on any database.', 1;");
}

[DbContext(typeof(BrokenFirstContext))]
[Migration("20260101000002_CreateNotes")]
public sealed class CreateNotes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Notes",
            columns: table => new { Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1") },
            constraints: table => table.PrimaryKey("PK_Notes", x => x.Id));
}

[DbContext(typeof(CheckedStatusContext))]
[Migration("20260101000002_AddUniqueTicketCode")]
public sealed class AddUniqueTicketCode : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex("IX_Tickets_Code", "Tickets", "Code", unique: true);
}

public sealed class RenameContext(DbContextOptions<RenameContext> options) : DbContext(options);

[DbContext(typeof(RenameContext))]
[Migration("20260101000001_CreateMembers")]
public sealed class CreateMembers : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateTable(
            name: "Members",
            columns: table => new
            {
                Id = table.Column<int>("int", nullable: false).Annotation("SqlServer:Identity", "1, 1"),
                Name = table.Column<string>("nvarchar(100)", maxLength: 100, nullable: true),
                Nickname = table.Column<string>("nvarchar(50)", maxLength: 50, nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_Members", x => x.Id));
}

// What EF scaffolds for renaming a nullable property and making it required. Nickname also becomes required,
// so the failure has to be matched to the right one of two NOT NULL scenarios by the column's new name.
[DbContext(typeof(RenameContext))]
[Migration("20260101000002_RenameMemberNameAndRequireIt")]
public sealed class RenameMemberNameAndRequireIt : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.RenameColumn(name: "Name", table: "Members", newName: "DisplayName");
        migrationBuilder.AlterColumn<string>(
            name: "DisplayName",
            table: "Members",
            type: "nvarchar(100)",
            maxLength: 100,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(100)",
            oldMaxLength: 100,
            oldNullable: true);
        migrationBuilder.AlterColumn<string>(
            name: "Nickname",
            table: "Members",
            type: "nvarchar(50)",
            maxLength: 50,
            nullable: false,
            oldClrType: typeof(string),
            oldType: "nvarchar(50)",
            oldMaxLength: 50,
            oldNullable: true);
    }
}

[DbContext(typeof(RenameContext))]
[Migration("20260101000003_MoveMembersToCrmWithUniqueName")]
public sealed class MoveMembersToCrmWithUniqueName : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema("crm");
        migrationBuilder.RenameTable(name: "Members", newName: "Subscribers", newSchema: "crm");
        migrationBuilder.CreateIndex("IX_Subscribers_DisplayName", "Subscribers", "DisplayName", schema: "crm", unique: true);
    }
}
