# MigrationRepair

Adversarial **EF Core** migration testing for **Microsoft SQL Server**: migrate the database to **N−1**, seed at least one valid row per table plus **deterministic dirty rows** derived from `Up()` (unique indexes, foreign keys, nullable→required, max-length shrink, check constraints), then run **`Migrate` to N**. If `Up()` fails, you get a summary and **T-SQL repair hints** (`RepairHintCatalog`).

**Target:** EF Core **10** / **.NET 10** (`Microsoft.EntityFrameworkCore.Relational` 10.0.0). Your app uses **`Microsoft.EntityFrameworkCore.SqlServer`**.

## Prerequisites

- SQL Server (localhost is fine) and a login that can **create/drop databases** (or pre-create empty databases used in connection strings).
- `DbContext` configured with **`UseSqlServer(...)`** and the same migrations you want to stress-test.

## Usage

```csharp
var optionsBuilder = new DbContextOptionsBuilder<YourDbContext>()
    .UseSqlServer("Server=localhost;Database=YourApp_Adversary;Integrated Security=True;TrustServerCertificate=True;");

var result = await MigrationAdversaryRunner.RunAsync(new MigrationAdversaryOptions
{
    CreateContext = () => new YourDbContext(optionsBuilder.Options),
    TargetMigrationId = "20260319120000_YourMigration",
    EnableFuzzing = false,
    FuzzSeed = 42,
    FuzzIterationCount = 8
});

if (!result.MigrationSucceeded)
{
    Assert.Fail(result.FailureSummary ?? result.Exception?.Message);
}
```

- **`ActiveProvider`**: optional; defaults to **`Microsoft.EntityFrameworkCore.SqlServer`** for capturing `MigrationBuilder` operations.

## Tests in this repo

Integration tests use **`TestSqlServerConfig.TestRunConnectionString`** (default: `Server=localhost;Database=MigrationRepair_Test;...`). Override with env **`MIGRATION_REPAIR_SQL_TEST`** if needed. Design-time **`dotnet ef`** uses **`MIGRATION_REPAIR_SQL_DESIGN`** or **`MigrationRepair_Design`**.

```bash
dotnet build MigrationRepair.slnx
dotnet test MigrationRepair.Tests/MigrationRepair.Tests.csproj
```

## Limitations

- **`migrationBuilder.Sql(...)`**: scenarios are inferred from **fluent operations** only; raw SQL may bypass analysis (`ContainsRawSqlInMigration` + warnings).
- **First migration**: tables missing at **N−1** skip table-dependent scenarios (`SkipReason` on `AdversaryScenario`).
- **Check constraints**: no automatic violating row; scenario points you to manual tests.
- **Schema**: introspection assumes **`dbo`** user tables (matches typical EF defaults).
- **Custom `Up()` ordering** or data-fix steps can cause **false negatives**.

## Nested test project

`MigrationRepair.Tests/` is excluded from the main library compile glob in `MigrationRepair.csproj`.

## Local tooling

`dotnet-tools.json` pins **dotnet-ef** 10.0.0 for migrations in the test project.
