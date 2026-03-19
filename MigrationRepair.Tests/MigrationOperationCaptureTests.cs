using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationRepair.Capture;
using MigrationRepair.Tests.Migrations;
using Xunit;

namespace MigrationRepair.Tests;

public sealed class MigrationOperationCaptureTests
{
    [Fact]
    public void Capture_AddUniqueCode_migration_records_unique_CreateIndex()
    {
        var migration = new AddUniqueCode();
        var ops = MigrationOperationCapture.Capture(migration, "Microsoft.EntityFrameworkCore.Sqlite");

        var unique = Assert.Single(ops.OfType<CreateIndexOperation>(), i => i.IsUnique);
        Assert.Equal("Items", unique.Table);
        Assert.Equal("Code", Assert.Single(unique.Columns));
    }
}
