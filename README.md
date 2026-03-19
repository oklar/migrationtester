# MigrationRepair

Adversarial **EF Core** migration testing: migrate the database to **N−1**, seed at least one valid row per table plus **deterministic dirty rows** derived from `Up()` operations (unique indexes, foreign keys, nullable→required, max-length shrink, check constraints), then run **`Migrate` to N**. If `Up()` fails, you get a summary and **portable SQL repair hints** (`RepairHintCatalog`), with extra wording for **MySQL**, **SQL Server**, and **PostgreSQL**.

**Target:** EF Core **10** / **.NET 10** (package `Microsoft.EntityFrameworkCore.Relational` 10.0.0).

## Usage

```csharp
var optionsBuilder = new DbContextOptionsBuilder<YourDbContext>()
    .UseSqlite($"Data Source={Path.Combine(Path.GetTempPath(), "adv.db")}");

var result = await MigrationAdversaryRunner.RunAsync(new MigrationAdversaryOptions
{
    CreateContext = () => new YourDbContext(optionsBuilder.Options),
    TargetMigrationId = "20260319120000_YourMigration", // full migration id
    EnableFuzzing = false, // optional: random legal inserts validated per attempt
    FuzzSeed = 42,
    FuzzIterationCount = 8
});

if (!result.MigrationSucceeded)
{
    Assert.Fail(result.FailureSummary ?? result.Exception?.Message);
}
```

- Set **`DatabaseFamily`** when you want explicit dialect hints (default **`Portable`** infers from provider).
- Set **`ActiveProvider`** if auto-detection is wrong (same string as for `MigrationBuilder`, e.g. `Microsoft.EntityFrameworkCore.Sqlite`).

## E2E / integration smoke

Full **Migrate** runs are sensitive to **SQLite pooling** and test host lifetime in some environments. This repo includes **unit tests** for capture + scenario planning; for a full pipeline smoke test, run the snippet above from a **console app** or a test host you control. Use a unique `Data Source` per run; the runner calls **`EnsureDeleted`** and clears **SQLite** connection pools when the family is SQLite—still assert **`MigrationSucceeded`** as in the sample.

## Limitations

- **`migrationBuilder.Sql(...)`**: scenarios are inferred from **fluent operations** only; raw SQL may **bypass** analysis—`result.ContainsRawSqlInMigration` and **warnings** flag this.
- **First migration**: tables that do not exist at **N−1** skip table-dependent scenarios (see `SkipReason` on `AdversaryScenario`).
- **Check constraints**: no automatic violating row generation; scenario is flagged with guidance to add manual tests.
- **SQL Server / PostgreSQL** schema introspection in the runner is not fully wired; **SQLite** and **MySQL** introspection paths are implemented for seed ordering/metadata.
- **Custom `Up()` ordering** or data-fix steps can produce **false negatives** (migration succeeds while naive adversarial data exists).

## Nested test project layout

The sample test project lives under `MigrationRepair.Tests/`. The main SDK project **excludes** that folder from its default compile glob so `MigrationRepair.csproj` does not pick up test sources.

## Building

```bash
dotnet build MigrationRepair.slnx
dotnet test MigrationRepair.Tests/MigrationRepair.Tests.csproj
```

## Local tooling

`dotnet-tools.json` pins **dotnet-ef** 10.0.0 for regenerating sample migrations in the test project.
