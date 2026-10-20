using MigrationRepair.Planning;
using MigrationRepair.Schema;
using MigrationRepair.Seeding;
using Xunit;
using static MigrationRepair.Tests.TestSchema;

namespace MigrationRepair.Tests;

public sealed class SeedSqlGeneratorTests
{
    [Fact]
    public void BaselineInsert_skips_columns_sql_server_fills_itself()
    {
        var table = Table("Docs",
            Col("Id", "int", identity: true),
            Col("Title", "nvarchar(100)"),
            Col("RowVersion", "rowversion"),
            Col("Total", "int", computed: true));

        var sql = SeedSqlGenerator.BaselineInsert(table);

        Assert.Equal("INSERT INTO [dbo].[Docs] ([Title]) VALUES (N'v1');", sql.Sql);
    }

    [Fact]
    public void BaselineInsert_uses_NULL_for_nullable_and_DEFAULT_for_defaulted_columns()
    {
        var table = Table("Items",
            Col("Id", "int", identity: true),
            Col("Name", "nvarchar(50)", nullable: true),
            Col("Status", "nvarchar(20)", hasDefault: true));

        var sql = SeedSqlGenerator.BaselineInsert(table);

        Assert.Equal("INSERT INTO [dbo].[Items] ([Name]) VALUES (NULL);", sql.Sql);
    }

    [Fact]
    public void BaselineInsert_takes_required_foreign_key_from_a_principal_row()
    {
        var orders = new TableMetadata
        {
            Name = new TableName("dbo", "Orders"),
            Columns = [Col("Id", "int", identity: true), Col("CustomerId", "int"), Col("Note", "nvarchar(100)", nullable: true)],
            ForeignKeys = [ForeignKey("CustomerId", "Customers")]
        };

        var sql = SeedSqlGenerator.BaselineInsert(orders);

        Assert.Equal(
            "INSERT INTO [dbo].[Orders] ([CustomerId], [Note]) SELECT TOP (1) [p0].[Id], NULL FROM [dbo].[Customers] AS [p0];",
            sql.Sql);
    }

    [Fact]
    public void BaselineInsert_leaves_nullable_foreign_key_NULL()
    {
        var orders = new TableMetadata
        {
            Name = new TableName("dbo", "Orders"),
            Columns = [Col("Id", "int", identity: true), Col("CustomerId", "int", nullable: true)],
            ForeignKeys = [ForeignKey("CustomerId", "Customers")]
        };

        var sql = SeedSqlGenerator.BaselineInsert(orders);

        Assert.Equal("INSERT INTO [dbo].[Orders] ([CustomerId]) VALUES (NULL);", sql.Sql);
    }

    [Fact]
    public void BaselineInsert_table_with_only_generated_columns_uses_DEFAULT_VALUES()
    {
        var sql = SeedSqlGenerator.BaselineInsert(Table("Counters", Col("Id", "int", identity: true)));

        Assert.Equal("INSERT INTO [dbo].[Counters] DEFAULT VALUES;", sql.Sql);
    }

    [Fact]
    public void BaselineInsert_fails_for_required_self_referencing_foreign_key()
    {
        var nodes = new TableMetadata
        {
            Name = new TableName("dbo", "Nodes"),
            Columns = [Col("Id", "int", identity: true), Col("ParentId", "int")],
            ForeignKeys = [ForeignKey("ParentId", "Nodes")]
        };

        var sql = SeedSqlGenerator.BaselineInsert(nodes);

        Assert.False(sql.Succeeded);
        Assert.Contains("self-referencing", sql.Failure);
    }

    [Fact]
    public void BaselineInsert_fails_for_type_it_cannot_generate()
    {
        var sql = SeedSqlGenerator.BaselineInsert(Table("T", Col("Id", "int", identity: true), Col("Val", "my_clr_type")));

        Assert.False(sql.Succeeded);
        Assert.Contains("'Val'", sql.Failure);
    }

    [Theory]
    [InlineData("uniqueidentifier", "'00000000-0000-0000-0000-000000000001'")]
    [InlineData("money", "1")]
    [InlineData("time", "'00:00:00'")]
    [InlineData("datetimeoffset", "'2000-01-01T00:00:00+00:00'")]
    [InlineData("datetime", "'20000101'")]
    [InlineData("datetime2", "'20000101'")]
    [InlineData("bit", "0")]
    [InlineData("decimal(18,6)", "1")]
    [InlineData("decimal(2,2)", "0.01")]
    [InlineData("nchar(1)", "N'a'")]
    [InlineData("nvarchar(max)", "N'v1'")]
    [InlineData("varbinary(100)", "0x01")]
    [InlineData("geography", "geography::Point(0, 1, 4326)")]
    public void BaselineInsert_generates_value_that_fits_the_type(string sqlType, string expected)
    {
        var sql = SeedSqlGenerator.BaselineInsert(Table("T", Col("Id", "int", identity: true), Col("Val", sqlType)));

        Assert.Equal($"INSERT INTO [dbo].[T] ([Val]) VALUES ({expected});", sql.Sql);
    }

    [Fact]
    public void CopyRow_keeps_key_columns_and_lets_identity_renumber()
    {
        var items = new TableMetadata
        {
            Name = new TableName("dbo", "Items"),
            Columns = [Col("Id", "int", identity: true), Col("Code", "nvarchar(50)")],
            UniqueKeys = [PrimaryKey("Id")]
        };

        var sql = SeedSqlGenerator.CopyRow(items, ["Code"], [], ordinal: 2);

        Assert.Equal("INSERT INTO [dbo].[Items] ([Code]) SELECT TOP (1) [Code] FROM [dbo].[Items];", sql.Sql);
    }

    [Fact]
    public void CopyRow_gives_non_identity_primary_key_a_new_value()
    {
        var accounts = new TableMetadata
        {
            Name = new TableName("dbo", "Accounts"),
            Columns = [Col("Id", "uniqueidentifier"), Col("Email", "nvarchar(200)")],
            UniqueKeys = [PrimaryKey("Id")]
        };

        var sql = SeedSqlGenerator.CopyRow(accounts, ["Email"], [], ordinal: 2);

        Assert.Equal(
            "INSERT INTO [dbo].[Accounts] ([Id], [Email]) SELECT TOP (1) '00000000-0000-0000-0000-000000000002', [Email] FROM [dbo].[Accounts];",
            sql.Sql);
    }

    [Fact]
    public void CopyRow_gives_other_existing_unique_keys_new_values_and_applies_overrides()
    {
        var items = new TableMetadata
        {
            Name = new TableName("dbo", "Items"),
            Columns = [Col("Id", "int", identity: true), Col("Code", "nvarchar(50)"), Col("Slug", "nvarchar(50)"), Col("Name", "nvarchar(50)", nullable: true)],
            UniqueKeys = [PrimaryKey("Id"), Unique("IX_Items_Slug", "Slug")]
        };

        var sql = SeedSqlGenerator.CopyRow(items, ["Code"], [new ColumnValue("Name", "NULL")], ordinal: 3);

        Assert.Equal(
            "INSERT INTO [dbo].[Items] ([Code], [Slug], [Name]) SELECT TOP (1) [Code], N'v3', NULL FROM [dbo].[Items];",
            sql.Sql);
    }

    [Fact]
    public void CopyRow_fails_when_unique_key_can_only_change_through_foreign_keys()
    {
        var postTags = new TableMetadata
        {
            Name = new TableName("dbo", "PostTags"),
            Columns = [Col("PostId", "int"), Col("TagId", "int")],
            UniqueKeys = [PrimaryKey("PostId", "TagId")],
            ForeignKeys = [ForeignKey("PostId", "Posts"), ForeignKey("TagId", "Tags")]
        };

        var sql = SeedSqlGenerator.CopyRow(postTags, [], [], ordinal: 2);

        Assert.False(sql.Succeeded);
        Assert.Contains("'PK'", sql.Failure);
    }

    [Fact]
    public void CopyRows_inserts_copies_of_one_source_row_with_fresh_values_per_copy()
    {
        var users = new TableMetadata
        {
            Name = new TableName("dbo", "Users"),
            Columns = [Col("Id", "uniqueidentifier"), Col("Email", "nvarchar(200)", nullable: true), Col("Name", "nvarchar(50)")],
            UniqueKeys = [PrimaryKey("Id")]
        };

        var sql = SeedSqlGenerator.CopyRows(users, ["Email"], [new ColumnValue("Email", "N'v1'")], [2, 3]);

        Assert.Equal(
            "INSERT INTO [dbo].[Users] ([Id], [Email], [Name]) SELECT "
            + "CASE [n].[k] WHEN 1 THEN '00000000-0000-0000-0000-000000000002' WHEN 2 THEN '00000000-0000-0000-0000-000000000003' END, "
            + "N'v1', [s].[Name] FROM (SELECT TOP (1) * FROM [dbo].[Users]) AS [s] CROSS JOIN (VALUES (1), (2)) AS [n]([k]);",
            sql.Sql);
    }

    [Fact]
    public void TryGetFilterValues_takes_foreign_key_filter_column_from_principal()
    {
        var users = new TableMetadata
        {
            Name = new TableName("dbo", "Users"),
            Columns = [Col("Id", "int", identity: true), Col("Email", "nvarchar(200)", nullable: true), Col("ProfileId", "int", nullable: true)],
            ForeignKeys = [ForeignKey("ProfileId", "Profiles")]
        };
        var scenario = Scenario(AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint, "ProfileId") with
        {
            NotNullFilterColumns = ["Email", "ProfileId"]
        };

        Assert.True(SeedSqlGenerator.TryGetFilterValues(scenario, users, out var values, out _));
        Assert.Equal(
            [new ColumnValue("Email", "N'v1'"), new ColumnValue("ProfileId", "(SELECT TOP (1) [Id] FROM [dbo].[Profiles])")],
            values);
    }

    [Fact]
    public void UpdateOneRow_sets_values_on_a_single_row()
    {
        string sql = SeedSqlGenerator.UpdateOneRow(Table("Items"), [new ColumnValue("Name", "NULL"), new ColumnValue("Qty", "32768")]);

        Assert.Equal("UPDATE TOP (1) [dbo].[Items] SET [Name] = NULL, [Qty] = 32768;", sql);
    }

    [Fact]
    public void TryGetBadValues_null_for_column_becoming_required()
    {
        var table = Table("Items", Col("Name", "nvarchar(50)", nullable: true));

        Assert.True(SeedSqlGenerator.TryGetBadValues(Scenario(AdversaryScenarioKind.NonNullableColumnWithNull, "Name"), table, out var values, out _));
        Assert.Equal([new ColumnValue("Name", "NULL")], values);
    }

    [Fact]
    public void TryGetBadValues_fails_when_column_is_already_not_null()
    {
        var table = Table("Items", Col("Name", "nvarchar(50)"));

        Assert.False(SeedSqlGenerator.TryGetBadValues(Scenario(AdversaryScenarioKind.NonNullableColumnWithNull, "Name"), table, out _, out string? failure));
        Assert.Contains("already NOT NULL", failure);
    }

    [Fact]
    public void TryGetBadValues_long_value_is_one_longer_than_new_max_and_fits_old_column()
    {
        var table = Table("Products", Col("Code", "nvarchar(100)"));
        var scenario = Scenario(AdversaryScenarioKind.MaxLengthExceededAfterShrink, "Code") with { NewMaxLength = 60 };

        Assert.True(SeedSqlGenerator.TryGetBadValues(scenario, table, out var values, out _));
        Assert.Equal("REPLICATE(CAST(N'x' AS nvarchar(max)), 61)", Assert.Single(values).Value);
    }

    [Fact]
    public void TryGetBadValues_long_value_fails_when_old_column_is_not_longer()
    {
        var table = Table("Products", Col("Code", "nvarchar(30)"));
        var scenario = Scenario(AdversaryScenarioKind.MaxLengthExceededAfterShrink, "Code") with { NewMaxLength = 60 };

        Assert.False(SeedSqlGenerator.TryGetBadValues(scenario, table, out _, out string? failure));
        Assert.Contains("cannot hold more than 60", failure);
    }

    [Theory]
    [InlineData("int", "2147483647")]
    [InlineData("tinyint", "255")]
    [InlineData("uniqueidentifier", "'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF'")]
    [InlineData("nvarchar(3)", "N'~'")]
    public void TryGetBadValues_orphan_value_fits_the_column_type(string type, string expected)
    {
        var table = Table("Orders", Col("CustomerId", type));

        Assert.True(SeedSqlGenerator.TryGetBadValues(Scenario(AdversaryScenarioKind.ForeignKeyOrphan, "CustomerId"), table, out var values, out _));
        Assert.Equal(expected, Assert.Single(values).Value);
    }

    [Fact]
    public void TryGetBadValues_conversion_uses_the_planned_value()
    {
        var table = Table("Measurements", Col("Quantity", "int"));
        var scenario = Scenario(AdversaryScenarioKind.ColumnTypeConversionIncompatible, "Quantity") with { SeedValue = "32768" };

        Assert.True(SeedSqlGenerator.TryGetBadValues(scenario, table, out var values, out _));
        Assert.Equal([new ColumnValue("Quantity", "32768")], values);
    }

    [Fact]
    public void TryGetBadValues_not_null_column_added_needs_no_values()
    {
        var table = Table("Orders", Col("Id", "int", identity: true));

        Assert.True(SeedSqlGenerator.TryGetBadValues(Scenario(AdversaryScenarioKind.NotNullColumnAddedWithoutDefault, "Status"), table, out var values, out _));
        Assert.Empty(values);
    }

    private static AdversaryScenario Scenario(AdversaryScenarioKind kind, string column) =>
        new() { Kind = kind, Name = "test", Table = "T", Columns = [column] };
}
