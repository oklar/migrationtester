# MigrationTester

NuGet library for **migration safety** on **Microsoft SQL Server**: in integration tests, stress each EF Core migration with **schema-aware “bad” data** so you find data-dependent `Up()` failures **before production**. The library migrates to **N−1**, seeds **baseline + adversarial** rows derived from migration **N**’s fluent `Up()` operations, then runs **`Migrate` to N**. Failures include **T-SQL repair hints** (`RepairHintCatalog`).

**Target:** EF Core **10** / **.NET 10** (`Microsoft.EntityFrameworkCore.Relational`). Your test and app projects reference **`Microsoft.EntityFrameworkCore.SqlServer`**.

## Why use this package?

- **Greenfield or long histories:** Whether you have two migrations or fifty, you can **loop the full chain** (or a subset) and see which steps break on realistic dirty data.
- **Deterministic scenarios:** **unique indexes / primary keys**, **foreign keys**, **nullable → required**, **max-length shrink**, **NOT NULL column without default**, **type conversions that can fail** (string → number/GUID/date, numeric narrowing such as `int` → `smallint`, `datetime2` → `datetime`), **decimal precision or scale changes that drop integer digits**, and **check constraint** guidance (check rows are not auto-generated).
- **Data that is valid before the migration:** every seeded row satisfies the N−1 schema (foreign keys, unique keys, lengths), so a failure points at the migration, not at the seed.
- **Honest limits:** Operations come from **`MigrationBuilder` fluency only**—`migrationBuilder.Sql(...)` is **flagged**, not parsed (false negatives possible). Introspection covers every user schema.

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
    TargetMigrationId = "20240101120000_MyMigration"
});

Assert.True(result.MigrationSucceeded, result.FailureSummary ?? result.Exception?.Message);
```

## Full history (recommended for NuGet consumers)

```csharp
var batch = new MigrationAdversaryBatchOptions
{
    CreateContext = () => new AppDbContext(opts.Options)
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
const string DockerSa = "Server=127.0.0.1,1433;User Id=sa;Password=Your_password1;TrustServerCertificate=True;Database=MigrationTester_Test";

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

## Working on this repo

| Path | Contents |
|------|----------|
| `src/MigrationTester/` | The library (the only packable project) |
| `tests/MigrationTester.Tests/` | Unit and SQL Server integration tests (xUnit.net v3) |
| `examples/MigrationTester.Examples/` | Consumer sample; see its [README](examples/MigrationTester.Examples/README.md) |

Shared build settings live in `Directory.Build.props`, package versions in `Directory.Packages.props`. Warnings, including NuGet vulnerability warnings, fail the build.

```bash
dotnet build MigrationTester.slnx
dotnet test MigrationTester.slnx
```

Integration tests need SQL Server. The defaults in **`TestSqlServerConfig`** use Windows auth against the default local instance; override them with **`MIGRATION_TESTER_SQL_TEST`** (tests) and **`MIGRATION_TESTER_SQL_DESIGN`** (`dotnet ef`). For a SQL Express named instance, for example:

```bash
export MIGRATION_TESTER_SQL_TEST='Server=localhost\SQLEXPRESS;Database=MigrationTester_Test;Integrated Security=True;TrustServerCertificate=True'
```

At the **start** of each run the **`MigrationTester_Test`** database is **dropped**; afterwards it stays as the last test left it, for inspection in SSMS.

**Schema cases.** `tests/MigrationTester.Tests/SchemaCases/` runs the adversary end to end over hand-written migrations for common schema shapes (rowversion columns, GUID keys, non-`dbo` schemas, required foreign keys, filtered unique indexes, type narrowing, seed rows rejected by a CHECK constraint). When you find a schema the adversary gets wrong, add it there as a test of the behavior you want and mark it `Explicit` until it is fixed; the default run skips explicit tests. Run them with:

```bash
dotnet test tests/MigrationTester.Tests -- xUnit.Explicit=only
```

The test project holds several `DbContext` types, so pass `--context TestDbContext` to `dotnet ef`.

**CI** (`.github/workflows/ci.yml`) runs the tests against a SQL Server container, packs the library, and builds the examples against the packed nupkg.

## Limitations

- **`migrationBuilder.Sql`**: not analyzed; **`ContainsRawSqlInMigration`** and warnings only.
- **Not exhaustive “any data”**: deterministic scenarios only; not full property-based generation.
- **SQL Server–only** implementation (provider alignment: test against the same engine as production migrations).
- **First migration**: no N−1 tables; uses an empty schema model until **`Migrate(N)`** creates the database.
- **Seeding**: one ordinary row per table, with bad values planted by single-row updates and row copies. When SQL Server rejects a seed statement (an existing CHECK constraint, a trigger, a column type with no generator, a required self-referencing foreign key), the affected scenarios are reported as skipped with **`Not seeded: <reason>`** and a warning is added; the run carries on. Filtered unique indexes are seeded when the filter is only `IS NOT NULL` terms (what EF generates for nullable columns); other filters and new CHECK constraints are not seeded.
- **Silent data loss** (e.g. `datetime2` → `date`, scale reduction, `nvarchar` → `varchar`) does not fail a migration, so it is not reported.
- **Custom `Up()` ordering** or data-fix steps can still yield **false negatives**.

## Local tooling

`dotnet-tools.json` pins **dotnet-ef** 10.0.13 for the test and sample migrations (`dotnet tool restore`).
