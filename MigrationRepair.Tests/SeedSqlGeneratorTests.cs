using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationRepair.Planning;
using MigrationRepair.Schema;
using MigrationRepair.Seeding;
using Xunit;

namespace MigrationRepair.Tests;

public sealed class SeedSqlGeneratorTests
{
    private static RelationalSchemaModel SingleTableSchema(
        string table,
        params TableColumnMetadata[] cols)
    {
        return new RelationalSchemaModel
        {
            TableNames = [table],
            ColumnsByTable = new Dictionary<string, IReadOnlyList<TableColumnMetadata>>(StringComparer.OrdinalIgnoreCase)
            {
                [table] = cols
            },
            ForeignKeys = []
        };
    }

    private static TableColumnMetadata Col(string table, string name, string type,
        int ordinal = 0, bool pk = false, bool identity = false, bool nullable = false) =>
        new()
        {
            TableName = table,
            Name = name,
            DeclaredType = type,
            Ordinal = ordinal,
            IsPrimaryKey = pk,
            IsAutoIncrement = identity,
            IsNullable = nullable
        };

    [Fact]
    public void BaselineInserts_generates_one_row_per_table()
    {
        var schema = SingleTableSchema("Items",
            Col("Items", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("Items", "Code", "nvarchar", ordinal: 1));

        var sql = SeedSqlGenerator.BaselineInserts(schema);

        var stmt = Assert.Single(sql);
        Assert.Contains("[Code]", stmt);
        Assert.DoesNotContain("[Id]", stmt);
    }

    [Fact]
    public void BaselineInserts_skips_nullable_columns_with_NULL()
    {
        var schema = SingleTableSchema("Items",
            Col("Items", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("Items", "Name", "nvarchar", ordinal: 1, nullable: true));

        var sql = SeedSqlGenerator.BaselineInserts(schema);

        var stmt = Assert.Single(sql);
        Assert.Contains("NULL", stmt);
    }

    [Fact]
    public void AdversarialStatements_duplicate_unique_generates_two_inserts()
    {
        var schema = SingleTableSchema("Items",
            Col("Items", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("Items", "Code", "nvarchar", ordinal: 1));

        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint,
            Name = "test",
            Table = "Items",
            Columns = ["Code"]
        };

        var sql = SeedSqlGenerator.AdversarialStatements(scenario, schema);

        Assert.Equal(2, sql.Count);
        Assert.All(sql, s => Assert.Contains("INSERT INTO [Items]", s));
        Assert.All(sql, s => Assert.Contains("adv-dup", s, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void AdversarialStatements_orphan_fk_generates_insert_with_nonexistent_key()
    {
        var schema = SingleTableSchema("Orders",
            Col("Orders", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("Orders", "CustomerId", "int", ordinal: 1));

        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.ForeignKeyOrphan,
            Name = "test",
            Table = "Orders",
            Columns = ["CustomerId"],
            PrincipalTable = "Customers",
            PrincipalColumns = ["Id"]
        };

        var sql = SeedSqlGenerator.AdversarialStatements(scenario, schema);

        var stmt = Assert.Single(sql);
        Assert.Contains("999999999", stmt);
    }

    [Fact]
    public void AdversarialStatements_null_column_generates_insert_with_NULL()
    {
        var schema = SingleTableSchema("Items",
            Col("Items", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("Items", "Name", "nvarchar", ordinal: 1, nullable: true));

        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.NonNullableColumnWithNull,
            Name = "test",
            Table = "Items",
            Columns = ["Name"]
        };

        var sql = SeedSqlGenerator.AdversarialStatements(scenario, schema);

        var stmt = Assert.Single(sql);
        Assert.Contains("NULL", stmt);
    }

    [Fact]
    public void AdversarialStatements_max_length_generates_update_with_long_value()
    {
        var schema = SingleTableSchema("Items",
            Col("Items", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("Items", "Code", "nvarchar", ordinal: 1));

        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.MaxLengthExceededAfterShrink,
            Name = "test",
            Table = "Items",
            Columns = ["Code"],
            NewMaxLength = 10
        };

        var sql = SeedSqlGenerator.AdversarialStatements(scenario, schema);

        var stmt = Assert.Single(sql);
        Assert.Contains("UPDATE [Items]", stmt);
        Assert.Contains("[Code]", stmt);
    }

    [Fact]
    public void AdversarialStatements_skipped_scenario_returns_empty()
    {
        var schema = SingleTableSchema("Items",
            Col("Items", "Id", "int", ordinal: 0, pk: true, identity: true));

        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint,
            Name = "test",
            Table = "Items",
            Columns = ["Code"],
            Skipped = true
        };

        var sql = SeedSqlGenerator.AdversarialStatements(scenario, schema);

        Assert.Empty(sql);
    }

    [Theory]
    [InlineData("uniqueidentifier", "00000000-0000-0000-0000-000000000001")]
    [InlineData("money", "1")]
    [InlineData("smallmoney", "1")]
    [InlineData("time", "00:00:00")]
    [InlineData("datetimeoffset", "2000-01-01T00:00:00+00:00")]
    [InlineData("datetime2", "2000-01-01")]
    [InlineData("int", "1")]
    [InlineData("bit", "0")]
    public void BaselineInserts_handles_sql_server_types(string sqlType, string expectedFragment)
    {
        var schema = SingleTableSchema("T",
            Col("T", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("T", "Val", sqlType, ordinal: 1));

        var sql = SeedSqlGenerator.BaselineInserts(schema);

        var stmt = Assert.Single(sql);
        Assert.Contains(expectedFragment, stmt);
    }

    [Fact]
    public void AdversarialStatements_type_conversion_generates_update_with_poison()
    {
        var schema = SingleTableSchema("Items",
            Col("Items", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("Items", "Code", "nvarchar", ordinal: 1));

        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.ColumnTypeConversionIncompatible,
            Name = "test",
            Table = "Items",
            Columns = ["Code"],
            OldColumnType = "nvarchar(100)",
            NewColumnType = "int"
        };

        var sql = SeedSqlGenerator.AdversarialStatements(scenario, schema);

        var stmt = Assert.Single(sql);
        Assert.Contains("UPDATE [Items]", stmt);
        Assert.Contains("[Code]", stmt);
        Assert.Contains("not_a_number", stmt, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AdversarialStatements_precision_overflow_generates_update_with_large_value()
    {
        var schema = SingleTableSchema("Items",
            Col("Items", "Id", "int", ordinal: 0, pk: true, identity: true),
            Col("Items", "Price", "decimal", ordinal: 1));

        var scenario = new AdversaryScenario
        {
            Kind = AdversaryScenarioKind.DecimalPrecisionOverflow,
            Name = "test",
            Table = "Items",
            Columns = ["Price"],
            NewPrecision = 10,
            NewScale = 2
        };

        var sql = SeedSqlGenerator.AdversarialStatements(scenario, schema);

        var stmt = Assert.Single(sql);
        Assert.Contains("UPDATE [Items]", stmt);
        Assert.Contains("[Price]", stmt);
        Assert.Contains("999999999.99", stmt);
    }

    [Theory]
    [InlineData("nvarchar(100)", "int", "__not_a_number__")]
    [InlineData("nvarchar(50)", "uniqueidentifier", "__not_a_guid__")]
    [InlineData("varchar(100)", "datetime", "__not_a_date__")]
    [InlineData("float", "int", "1.5")]
    [InlineData("nvarchar(10)", "bit", "maybe")]
    public void PoisonLiteral_returns_appropriate_value_for_type_transition(
        string oldType, string newType, string expectedFragment)
    {
        string result = SeedSqlGenerator.PoisonLiteral(oldType, newType);

        Assert.Contains(expectedFragment, result, StringComparison.OrdinalIgnoreCase);
    }
}
