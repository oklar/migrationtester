# MigrationRepair

NuGet library for **migration safety** on **Microsoft SQL Server**: in integration tests, stress each EF Core migration with **schema-aware “bad” data** so you find data-dependent `Up()` failures **before production**. The library migrates to **N−1**, seeds **baseline + adversarial** rows derived from migration **N**’s fluent `Up()` operations, then runs **`Migrate` to N**. Failures include **T-SQL repair hints** (`RepairHintCatalog`).

**Target:** EF Core **10** / **.NET 10** (`Microsoft.EntityFrameworkCore.Relational`). Your test and app projects reference **`Microsoft.EntityFrameworkCore.SqlServer`**.

## Why use this package?

- **Greenfield or long histories:** Whether you have two migrations or fifty, you can **loop the full chain** (or a subset) and see which steps break on realistic dirty data.
- **Deterministic + optional fuzz:** Built-in scenarios cover **unique indexes**, **foreign keys**, **nullable → required**, **max-length shrink**, and **check** guidance; optional fuzz adds extra legal inserts.
- **Honest limits:** Operations come from **`MigrationBuilder` fluency only**—`migrationBuilder.Sql(...)` is **flagged**, not parsed (false negatives possible). Introspection targets **`dbo`** user tables.

## Prerequisites

- SQL Server reachable from tests (localhost, Docker, or a dedicated instance) and a login that can **create/drop** the test database.
- `DbContext` with **`UseSqlServer(...)`** and the same migrations you ship.

## Single migration

```csharp
var cs = Environment.GetEnvironmentVariable("TEST_SQL") ?? "Server=127.0.0.1,1433;User Id=sa;Password=...;TrustServerCertificate=True";
var opts = new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(cs + ";Database=MyApp_MigrationAdversary");

var result = await MigrationAdversaryRunner.RunAsync(new MigrationAdversaryOptions
{
    CreateContext = () => new AppDbContext(opts.Options),
    TargetMigrationId = "20240101120000_MyMigration",
    EnableFuzzing = false
});

Assert.True(result.MigrationSucceeded, result.FailureSummary ?? result.Exception?.Message);
```

## Full history (recommended for NuGet consumers)

```csharp
var batch = new MigrationAdversaryBatchOptions
{
    CreateContext = () => new AppDbContext(opts.Options),
    EnableFuzzing = false
    // MigrationIds = null  → every migration from Database.GetMigrations() in order
};

var steps = await MigrationAdversaryRunner.RunAllAsync(batch);

Assert.True(
    MigrationAdversaryRunner.AllStepsSucceeded(steps),
    MigrationAdversaryRunner.FirstFailedStep(steps)?.Result.FailureSummary);
```

- **Subset (canonical order):** set `MigrationIds` to filter; order follows **EF apply order**, not your list order.
- **CI shard:** use **`RunRangeAsync(batch, fromId, toId)`** on contiguous segments (**cannot** be combined with `MigrationIds`). Each step is an isolated run (`EnsureDeleted`, seed, migrate), same as calling **`RunAsync`** per id.

## Integration test recipe (Docker SQL Server)

No Testcontainers dependency in this package; point **`UseSqlServer`** at any instance. Example: run SQL Server in Docker, map **1433**, then:

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_password1" -p 1433:1433 --name sqladv -d mcr.microsoft.com/mssql/server:2022-latest
```

```csharp
const string DockerSa = "Server=127.0.0.1,1433;User Id=sa;Password=Your_password1;TrustServerCertificate=True;Database=MigrationRepair_Test";

var batch = new MigrationAdversaryBatchOptions
{
    CreateContext = () => new AppDbContext(
        new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(DockerSa).Options)
};

var steps = await MigrationAdversaryRunner.RunAllAsync(batch);
Assert.True(MigrationAdversaryRunner.AllStepsSucceeded(steps));
```

Use **Windows auth** on dev (`Integrated Security=True`) when you prefer SSMS-style local instances; override with env vars in CI (see repo `TestSqlServerConfig`).

## API summary

| Member | Purpose |
|--------|---------|
| `RunAsync(MigrationAdversaryOptions)` | One migration N with fresh DB |
| `RunAllAsync(MigrationAdversaryBatchOptions)` | All migrations, or filtered by `MigrationIds` |
| `RunRangeAsync(batch, from, to)` | Inclusive slice of apply order (sharding) |
| `MigrationStepResult` | `MigrationId`, `Result`, `Duration` per step |
| `AllStepsSucceeded` / `FirstFailedStep` | Test helpers |

## Tests in this repo

```bash
dotnet build MigrationRepair.slnx
dotnet test MigrationRepair.Tests/MigrationRepair.Tests.csproj
```

Defaults: **`TestSqlServerConfig`** (`MIGRATION_REPAIR_SQL_TEST`, **`MIGRATION_REPAIR_SQL_DESIGN`** for `dotnet ef`).

## Limitations

- **`migrationBuilder.Sql`**: not analyzed; **`ContainsRawSqlInMigration`** and warnings only.
- **Not exhaustive “any data”**: deterministic scenarios + optional fuzz; not full property-based generation.
- **SQL Server–only** implementation (provider alignment: test against the same engine as production migrations).
- **First migration**: no N−1 tables; uses an empty schema model until **`Migrate(N)`** creates the database.
- **Custom `Up()` ordering** or data-fix steps can still yield **false negatives**.

## Nested test project

`MigrationRepair.Tests/` is excluded from the main library’s compile glob in `MigrationRepair.csproj`.

## Local tooling

`dotnet-tools.json` pins **dotnet-ef** 10.0.0 for sample migrations.
