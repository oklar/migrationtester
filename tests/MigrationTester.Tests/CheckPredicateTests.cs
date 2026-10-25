using System.Globalization;
using MigrationTester.Planning;
using Xunit;
using static MigrationTester.Tests.TestSchema;

namespace MigrationTester.Tests;

public sealed class CheckPredicateTests
{
    [Theory]
    [InlineData("[Price] > 0", "Price", "GreaterThan 0")]
    [InlineData("[Quantity] >= 1", "Quantity", "GreaterThanOrEqual 1")]
    [InlineData("[Status] IN (N'Open', N'Closed')", "Status", "In 'Open','Closed'")]
    [InlineData("[Qty] >= 1 AND [Qty] <= 100", "Qty", "GreaterThanOrEqual 1 AND LessThanOrEqual 100")]
    [InlineData("[Email] IS NOT NULL", "Email", "IsNotNull")]
    [InlineData("[Email] IS NOT NULL AND [Email] <> N''", "Email", "IsNotNull AND NotEqual ''")]
    // The form SQL Server stores in sys.check_constraints.
    [InlineData("([Price]>(0))", "Price", "GreaterThan 0")]
    [InlineData("([Qty]>=(1) AND [Qty]<=(100))", "Qty", "GreaterThanOrEqual 1 AND LessThanOrEqual 100")]
    [InlineData("([Delta]>(-(1)))", "Delta", "GreaterThan -1")]
    [InlineData("([Qty] > 0) AND ([Qty] < 10)", "Qty", "GreaterThan 0 AND LessThan 10")]
    [InlineData("([Price]) > 0", "Price", "GreaterThan 0")]
    [InlineData("0 < [Price]", "Price", "GreaterThan 0")]
    [InlineData("100 >= [Qty]", "Qty", "LessThanOrEqual 100")]
    [InlineData("Price <> -1.5", "Price", "NotEqual -1.5")]
    [InlineData("\"Price\" != .5", "Price", "NotEqual 0.5")]
    [InlineData("[Odd]]Name] = 1", "Odd]Name", "Equal 1")]
    [InlineData("[Name] <> N'It''s'", "Name", "NotEqual 'It's'")]
    [InlineData("[Code] = 'abc'", "Code", "Equal 'abc'")]
    [InlineData("[qty] > 0 and [QTY] < 10", "qty", "GreaterThan 0 AND LessThan 10")]
    public void TryParse_accepts_one_column_compared_with_literals(string sql, string column, string expected)
    {
        Assert.True(CheckPredicate.TryParse(sql, out var predicate));
        Assert.Equal(column, predicate.Column);
        Assert.Equal(expected, Describe(predicate));
    }

    [Theory]
    [InlineData("LEN([Code]) = 3")]
    [InlineData("[Start] < [End]")]
    [InlineData("[Price] > 0 OR [Price] < -10")]
    [InlineData("[Price] > 0 AND [Quantity] > 0")]
    [InlineData("NOT [Price] = 0")]
    [InlineData("[Price] IS NULL")]
    [InlineData("[Price] = NULL")]
    [InlineData("[Price] BETWEEN 1 AND 5")]
    [InlineData("[Code] LIKE N'A%'")]
    [InlineData("[Price] NOT IN (1, 2)")]
    [InlineData("[Price] IN (1, N'x')")]
    [InlineData("[Price] IN ()")]
    [InlineData("[Price] > 1e5")]
    [InlineData("[Price] > 0x10")]
    [InlineData("[Price] !< 0")]
    [InlineData("[Price] > 0 -- and more")]
    [InlineData("[Price] > /* 1 */ 0")]
    [InlineData("[Price] > 0)")]
    [InlineData("([Price] > 0")]
    [InlineData("[Price] > 0 AND")]
    [InlineData("[Code] = 'unterminated")]
    [InlineData("1 = 1")]
    [InlineData("[Price]")]
    [InlineData("")]
    [InlineData(null)]
    public void TryParse_rejects_other_predicates(string? sql)
    {
        Assert.False(CheckPredicate.TryParse(sql, out _));
    }

    [Theory]
    [InlineData("[Price] > 0", "decimal(10,2)", "0")]
    [InlineData("[Price] > 0.005", "decimal(10,2)", "0.00")]
    [InlineData("[Price] >= 1", "decimal(10,2)", "0.99")]
    [InlineData("[Quantity] >= 1", "int", "0")]
    [InlineData("[Quantity] > 0.5", "int", "0")]
    [InlineData("[Quantity] < 100", "int", "100")]
    [InlineData("[Quantity] <= 100", "int", "101")]
    [InlineData("[Quantity] = 7", "smallint", "8")]
    [InlineData("[Quantity] IN (1, 2, 3)", "int", "4")]
    [InlineData("[Quantity] <> 0", "bigint", "0")]
    [InlineData("[Quantity] >= 0 AND [Quantity] <= 100", "tinyint", "101")]
    [InlineData("[Flag] = 1", "bit", "0")]
    [InlineData("[Amount] > 0", "money", "0")]
    [InlineData("[Amount] >= 0", "smallmoney", "-0.0001")]
    [InlineData("[Ratio] <= 1", "float", "2")]
    [InlineData("0 < [Price]", "decimal(18,2)", "0")]
    [InlineData("[Status] IN (N'Open', N'Closed')", "nvarchar(20)", "N'x'")]
    [InlineData("[Code] IN ('x', 'X  ', N'ｙ')", "varchar(5)", "N'z'")]
    [InlineData("[Code] = N'abc'", "nchar(3)", "N'x'")]
    [InlineData("[Code] <> ''", "nvarchar(10)", "N''")]
    [InlineData("[Code] <> N'It''s'", "varchar(10)", "N'It''s'")]
    [InlineData("[Code] > N'm'", "nvarchar(max)", "N'm'")]
    public void ViolatingValue_fits_the_column_and_breaks_the_predicate(string sql, string type, string expected)
    {
        Assert.True(CheckPredicate.TryParse(sql, out var predicate));

        Assert.Equal(expected, predicate.ViolatingValue(Col(predicate.Column, type)));
    }

    [Fact]
    public void ViolatingValue_is_NULL_for_IS_NOT_NULL_on_a_nullable_column()
    {
        Assert.True(CheckPredicate.TryParse("[Email] IS NOT NULL AND [Email] <> N''", out var predicate));

        Assert.Equal("NULL", predicate.ViolatingValue(Col("Email", "nvarchar(200)", nullable: true)));
        Assert.Equal("N''", predicate.ViolatingValue(Col("Email", "nvarchar(200)")));
    }

    [Theory]
    // Every value the column holds passes.
    [InlineData("[Quantity] >= 0", "tinyint")]
    [InlineData("[Flag] IN (0, 1)", "bit")]
    [InlineData("[Quantity] <> 0.5", "int")]
    [InlineData("[Price] < 1000", "decimal(3,0)")]
    [InlineData("[Code] <> N'TOOLONG'", "nvarchar(3)")]
    [InlineData("[Email] IS NOT NULL", "nvarchar(200)")]
    // Literal of another type than the column.
    [InlineData("[Price] > 0", "nvarchar(10)")]
    [InlineData("[Code] = N'A'", "int")]
    // Ordering of strings depends on the collation.
    [InlineData("[Code] >= N'A'", "nvarchar(10)")]
    // A varchar column could store the literal differently.
    [InlineData("[Code] <> N'Æsir'", "varchar(10)")]
    // Column types without generators.
    [InlineData("[Start] > '2020-01-01'", "datetime2")]
    [InlineData("[Notes] <> N''", "ntext")]
    public void ViolatingValue_is_null_when_no_value_is_known(string sql, string type)
    {
        Assert.True(CheckPredicate.TryParse(sql, out var predicate));

        Assert.Null(predicate.ViolatingValue(Col(predicate.Column, type)));
    }

    private static string Describe(CheckPredicate predicate) =>
        string.Join(" AND ", predicate.Terms.Select(static t =>
            $"{t.Operator} {string.Join(",", t.Literals.Select(static l => l.Number?.ToString(CultureInfo.InvariantCulture) ?? $"'{l.Text}'"))}".TrimEnd()));
}
