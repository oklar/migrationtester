using Microsoft.EntityFrameworkCore.Migrations.Operations;
using MigrationTester.Runner;
using MigrationTester.Tests.Migrations;
using Xunit;

namespace MigrationTester.Tests;

public sealed class MigrationOperationCaptureTests
{
    [Fact]
    public void Capture_AddUniqueCode_migration_records_unique_CreateIndex()
    {
        var migration = new AddUniqueCode();
        var ops = MigrationOperationCapture.Capture(migration, ActiveProviderResolver.SqlServerProvider);

        var unique = Assert.Single(ops.OfType<CreateIndexOperation>(), i => i.IsUnique);
        Assert.Equal("Items", unique.Table);
        Assert.Equal("Code", Assert.Single(unique.Columns));
    }
}
