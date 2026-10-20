using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationRepair.Planning;
using MigrationRepair.Schema;
using Xunit;

namespace MigrationRepair.Tests;

public sealed class ScenarioPlannerTests
{
    [Fact]
    public void Plan_unique_index_produces_duplicate_scenario_when_table_exists()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation
            {
                Name = "IX_Items_Code",
                Table = "Items",
                Columns = ["Code"],
                IsUnique = true
            }
        ];

        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint, s.Kind);
        Assert.False(s.Skipped);
    }

    [Fact]
    public void Plan_unique_index_skipped_when_table_missing_at_N_minus_1()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation
            {
                Name = "IX_Items_Code",
                Table = "Items",
                Columns = ["Code"],
                IsUnique = true
            }
        ];

        var scenarios = ScenarioPlanner.Plan(ops, TestSchema.WithTables());
        var s = Assert.Single(scenarios);
        Assert.True(s.Skipped);
    }

    [Fact]
    public void Plan_foreign_key_produces_orphan_scenario()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddForeignKeyOperation
            {
                Name = "FK_Orders_Customers",
                Table = "Orders",
                Columns = ["CustomerId"],
                PrincipalTable = "Customers",
                PrincipalColumns = ["Id"]
            }
        ];

        var schema = TestSchema.WithTables("Orders", "Customers");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.ForeignKeyOrphan, s.Kind);
        Assert.False(s.Skipped);
        Assert.Equal("Orders", s.Table);
        Assert.Equal("Customers", s.PrincipalTable);
    }

    [Fact]
    public void Plan_foreign_key_skipped_when_dependent_table_missing()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddForeignKeyOperation
            {
                Name = "FK_Orders_Customers",
                Table = "Orders",
                Columns = ["CustomerId"],
                PrincipalTable = "Customers",
                PrincipalColumns = ["Id"]
            }
        ];

        var schema = TestSchema.WithTables("Customers");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.True(s.Skipped);
    }

    [Fact]
    public void Plan_alter_column_nullable_to_required_produces_scenario()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Description",
            Table = "Items",
            IsNullable = false
        };
        alter.OldColumn.IsNullable = true;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.NonNullableColumnWithNull, s.Kind);
        Assert.False(s.Skipped);
        Assert.Equal("Description", Assert.Single(s.Columns));
    }

    [Fact]
    public void Plan_alter_column_shrink_max_length_produces_scenario()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Code",
            Table = "Items",
            MaxLength = 50
        };
        alter.OldColumn.MaxLength = 200;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.MaxLengthExceededAfterShrink, s.Kind);
        Assert.Equal(50, s.NewMaxLength);
        Assert.Equal(200, s.OldMaxLength);
    }

    [Fact]
    public void Plan_alter_column_nullable_to_required_and_shrink_produces_two_scenarios()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Code",
            Table = "Items",
            IsNullable = false,
            MaxLength = 50
        };
        alter.OldColumn.IsNullable = true;
        alter.OldColumn.MaxLength = 200;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        Assert.Equal(2, scenarios.Count);
        Assert.Contains(scenarios, s => s.Kind == AdversaryScenarioKind.NonNullableColumnWithNull);
        Assert.Contains(scenarios, s => s.Kind == AdversaryScenarioKind.MaxLengthExceededAfterShrink);
    }

    [Fact]
    public void Plan_check_constraint_always_skipped()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddCheckConstraintOperation
            {
                Name = "CK_Items_PositiveQty",
                Table = "Items",
                Sql = "[Qty] > 0"
            }
        ];

        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.CheckConstraintViolation, s.Kind);
        Assert.True(s.Skipped);
        Assert.NotNull(s.SkipReason);
    }

    [Fact]
    public void Plan_non_unique_index_is_ignored()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation
            {
                Name = "IX_Items_Name",
                Table = "Items",
                Columns = ["Name"],
                IsUnique = false
            }
        ];

        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        Assert.Empty(scenarios);
    }

    [Fact]
    public void Plan_add_not_null_column_without_default_produces_scenario()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddColumnOperation
            {
                Name = "Status",
                Table = "Items",
                ClrType = typeof(int),
                IsNullable = false,
                DefaultValue = null,
                DefaultValueSql = null
            }
        ];

        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.NotNullColumnAddedWithoutDefault, s.Kind);
        Assert.False(s.Skipped);
        Assert.Equal("Status", Assert.Single(s.Columns));
    }

    [Fact]
    public void Plan_add_not_null_column_with_default_is_ignored()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddColumnOperation
            {
                Name = "Status",
                Table = "Items",
                ClrType = typeof(int),
                IsNullable = false,
                DefaultValue = 0
            }
        ];

        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        Assert.Empty(scenarios);
    }

    [Fact]
    public void Plan_add_nullable_column_is_ignored()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddColumnOperation
            {
                Name = "Notes",
                Table = "Items",
                ClrType = typeof(string),
                IsNullable = true
            }
        ];

        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        Assert.Empty(scenarios);
    }

    [Fact]
    public void Plan_alter_column_type_change_produces_type_conversion_scenario()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Code",
            Table = "Items",
            ColumnType = "int"
        };
        alter.OldColumn.ColumnType = "nvarchar(100)";

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.ColumnTypeConversionIncompatible, s.Kind);
        Assert.False(s.Skipped);
        Assert.Equal("nvarchar(100)", s.OldColumnType);
        Assert.Equal("int", s.NewColumnType);
    }

    [Fact]
    public void Plan_alter_column_same_type_does_not_produce_type_conversion()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Code",
            Table = "Items",
            ColumnType = "nvarchar(200)",
            IsNullable = false
        };
        alter.OldColumn.ColumnType = "nvarchar(100)";
        alter.OldColumn.IsNullable = true;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        Assert.DoesNotContain(scenarios, s => s.Kind == AdversaryScenarioKind.ColumnTypeConversionIncompatible);
    }

    [Fact]
    public void Plan_alter_column_precision_shrink_produces_overflow_scenario()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Price",
            Table = "Items",
            ColumnType = "decimal",
            Precision = 10,
            Scale = 2
        };
        alter.OldColumn.ColumnType = "decimal";
        alter.OldColumn.Precision = 18;
        alter.OldColumn.Scale = 6;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.DecimalPrecisionOverflow, s.Kind);
        Assert.Equal(18, s.OldPrecision);
        Assert.Equal(10, s.NewPrecision);
        Assert.Equal(6, s.OldScale);
        Assert.Equal(2, s.NewScale);
    }

    [Fact]
    public void Plan_alter_column_precision_grow_does_not_produce_overflow()
    {
        var alter = new AlterColumnOperation
        {
            Name = "Price",
            Table = "Items",
            ColumnType = "decimal",
            Precision = 18,
            Scale = 6
        };
        alter.OldColumn.ColumnType = "decimal";
        alter.OldColumn.Precision = 10;
        alter.OldColumn.Scale = 2;

        var ops = (IReadOnlyList<MigrationOperation>)[alter];
        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        Assert.Empty(scenarios);
    }

    [Fact]
    public void Plan_add_primary_key_produces_duplicate_scenario()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddPrimaryKeyOperation
            {
                Name = "PK_Items",
                Table = "Items",
                Columns = ["Code"]
            }
        ];

        var schema = TestSchema.WithTables("Items");
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.DuplicateUniqueIndexOrConstraint, s.Kind);
        Assert.False(s.Skipped);
        Assert.Contains("primary key", s.Name, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_add_primary_key_skipped_when_table_missing()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new AddPrimaryKeyOperation
            {
                Name = "PK_Items",
                Table = "Items",
                Columns = ["Code"]
            }
        ];

        var schema = TestSchema.WithTables();
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.True(s.Skipped);
    }

    [Fact]
    public void Plan_resolves_table_in_the_operation_schema()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation { Name = "IX_Items_Code", Schema = "app", Table = "Items", Columns = ["Code"], IsUnique = true }
        ];

        var schema = TestSchema.Of(new TableMetadata
        {
            Name = new TableName("app", "Items"),
            Columns = [TestSchema.Col("Code", "nvarchar(50)")]
        });
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.False(s.Skipped, s.SkipReason);
        Assert.Equal("app", s.Schema);
    }

    [Fact]
    public void Plan_unqualified_operation_does_not_match_table_in_other_schema()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation { Name = "IX_Items_Code", Table = "Items", Columns = ["Code"], IsUnique = true }
        ];

        var schema = TestSchema.Of(new TableMetadata { Name = new TableName("app", "Items"), Columns = [] });
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        Assert.True(Assert.Single(scenarios).Skipped);
    }

    [Fact]
    public void Plan_is_not_null_filtered_unique_index_produces_scenario_with_filter_columns()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation { Name = "IX_Users_Email", Table = "Users", Columns = ["Email"], IsUnique = true, Filter = "[Email] IS NOT NULL" }
        ];

        var scenarios = ScenarioPlanner.Plan(ops, TestSchema.WithTables("Users"));

        var s = Assert.Single(scenarios);
        Assert.False(s.Skipped, s.SkipReason);
        Assert.Equal(["Email"], s.NotNullFilterColumns);
    }

    [Fact]
    public void Plan_unique_index_with_other_filter_is_skipped()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation { Name = "IX_Users_Email", Table = "Users", Columns = ["Email"], IsUnique = true, Filter = "[Deleted] = 0" }
        ];

        var scenarios = ScenarioPlanner.Plan(ops, TestSchema.WithTables("Users"));

        var s = Assert.Single(scenarios);
        Assert.True(s.Skipped);
        Assert.Contains("[Deleted] = 0", s.SkipReason);
    }

    [Fact]
    public void Plan_filtered_unique_index_skipped_when_same_filtered_key_exists()
    {
        // EF renames an index by dropping and recreating it; the old one already rules out duplicates.
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new DropIndexOperation { Name = "IX_Users_Email", Table = "Users" },
            new CreateIndexOperation { Name = "IX_Users_EmailAddress", Table = "Users", Columns = ["Email"], IsUnique = true, Filter = "[Email] IS NOT NULL" }
        ];

        var schema = TestSchema.Of(new TableMetadata
        {
            Name = new TableName("dbo", "Users"),
            Columns = [TestSchema.Col("Email", "nvarchar(200)", nullable: true)],
            UniqueKeys = [new UniqueKeyMetadata { Name = "IX_Users_Email", Columns = ["Email"], Filter = "([Email] IS NOT NULL)" }]
        });
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.True(s.Skipped);
        Assert.Contains("IX_Users_Email", s.SkipReason);
    }

    [Fact]
    public void Plan_unique_index_skipped_when_existing_unique_key_already_covers_columns()
    {
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            new CreateIndexOperation { Name = "IX_Items_Id_Code", Table = "Items", Columns = ["Id", "Code"], IsUnique = true }
        ];

        var items = TestSchema.Table("Items", TestSchema.Col("Id", "int", identity: true), TestSchema.Col("Code", "nvarchar(50)"));
        var schema = TestSchema.Of(new TableMetadata
        {
            Name = items.Name,
            Columns = items.Columns,
            UniqueKeys = [TestSchema.PrimaryKey("Id")]
        });
        var scenarios = ScenarioPlanner.Plan(ops, schema);

        var s = Assert.Single(scenarios);
        Assert.True(s.Skipped);
        Assert.Contains("already prevents duplicates", s.SkipReason);
    }

    [Theory]
    [InlineData("int", "smallint", "32768")]
    [InlineData("bigint", "int", "2147483648")]
    [InlineData("int", "decimal(5,2)", "1000")]
    public void Plan_numeric_narrowing_produces_type_conversion_with_overflowing_value(string oldType, string newType, string seed)
    {
        var alter = new AlterColumnOperation { Name = "Qty", Table = "Items", ColumnType = newType };
        alter.OldColumn.ColumnType = oldType;

        var scenarios = ScenarioPlanner.Plan([alter], TestSchema.WithTables("Items"));

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.ColumnTypeConversionIncompatible, s.Kind);
        Assert.Equal(seed, s.SeedValue);
    }

    [Fact]
    public void Plan_decimal_scale_increase_that_drops_integer_digits_produces_overflow_scenario()
    {
        var alter = new AlterColumnOperation { Name = "Amount", Table = "Items", ColumnType = "decimal(18,4)" };
        alter.OldColumn.ColumnType = "decimal(18,2)";

        var scenarios = ScenarioPlanner.Plan([alter], TestSchema.WithTables("Items"));

        var s = Assert.Single(scenarios);
        Assert.Equal(AdversaryScenarioKind.DecimalPrecisionOverflow, s.Kind);
        Assert.Equal("100000000000000", s.SeedValue);
    }

    [Theory]
    [InlineData("int", "bit")]
    [InlineData("datetime2", "date")]
    [InlineData("decimal(18,4)", "decimal(18,2)")]
    [InlineData("nvarchar(100)", "varchar(100)")]
    public void Plan_conversions_that_cannot_fail_produce_no_scenario(string oldType, string newType)
    {
        var alter = new AlterColumnOperation { Name = "Value", Table = "Items", ColumnType = newType };
        alter.OldColumn.ColumnType = oldType;

        var scenarios = ScenarioPlanner.Plan([alter], TestSchema.WithTables("Items"));

        Assert.Empty(scenarios);
    }

    [Fact]
    public void Plan_prefers_live_column_over_operation_old_column()
    {
        // The operation claims nvarchar(200), but the database column is nvarchar(30): nothing to shrink.
        var alter = new AlterColumnOperation { Name = "Code", Table = "Items", ColumnType = "nvarchar(50)", MaxLength = 50 };
        alter.OldColumn.ColumnType = "nvarchar(200)";
        alter.OldColumn.MaxLength = 200;

        var schema = TestSchema.Of(TestSchema.Table("Items", TestSchema.Col("Code", "nvarchar(30)")));
        var scenarios = ScenarioPlanner.Plan([alter], schema);

        Assert.Empty(scenarios);
    }

    [Fact]
    public void Plan_add_identity_rowversion_or_computed_column_is_ignored()
    {
        var identity = new AddColumnOperation { Name = "Seq", Table = "Items", ClrType = typeof(int), IsNullable = false };
        identity.AddAnnotation("SqlServer:Identity", "1, 1");
        var ops = (IReadOnlyList<MigrationOperation>)
        [
            identity,
            new AddColumnOperation { Name = "Version", Table = "Items", ClrType = typeof(byte[]), IsRowVersion = true },
            new AddColumnOperation { Name = "Total", Table = "Items", ClrType = typeof(int), ComputedColumnSql = "[A] + [B]" }
        ];

        var scenarios = ScenarioPlanner.Plan(ops, TestSchema.WithTables("Items"));

        Assert.Empty(scenarios);
    }
}
