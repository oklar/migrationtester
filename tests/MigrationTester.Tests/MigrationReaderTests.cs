using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Runner;
using Xunit;

namespace MigrationTester.Tests;

public sealed class MigrationReaderTests
{
    // Reading migrations needs a configured provider but no database.
    private static TestDbContext Context() =>
        new(new DbContextOptionsBuilder<TestDbContext>().UseSqlServer("Server=unused;Database=unused").Options);

    [Fact]
    public void UpOperations_reads_unique_CreateIndex_from_AddUniqueCode()
    {
        using var context = Context();

        var ops = MigrationReader.UpOperations(context, MigrationReader.Resolve(context, "AddUniqueCode"));

        var unique = Assert.Single(ops.OfType<CreateIndexOperation>(), i => i.IsUnique);
        Assert.Equal("Items", unique.Table);
        Assert.Equal("Code", Assert.Single(unique.Columns));
    }

    [Fact]
    public void Resolve_accepts_full_id_or_name_and_rejects_unknown()
    {
        using var context = Context();

        Assert.Equal("20260319180738_AddUniqueCode", MigrationReader.Resolve(context, "20260319180738_AddUniqueCode"));
        Assert.Equal("20260319180738_AddUniqueCode", MigrationReader.Resolve(context, "AddUniqueCode"));
        Assert.Throws<ArgumentException>(() => MigrationReader.Resolve(context, "NoSuchMigration"));
    }

    [Fact]
    public void GetMigrationIds_returns_apply_order()
    {
        var ids = MigrationTestRunner.GetMigrationIds(Context);

        Assert.Equal(["20260319180720_Initial", "20260319180738_AddUniqueCode", "20260319190000_AddDescription"], ids);
    }
}
