# MigrationTester

Tests EF Core migrations against SQL Server with the kind of data production already has. For each migration N, MigrationTester migrates a fresh test database to N−1, plants rows that are valid there but that N's `Up()` may not handle, then migrates to N. When the migration fails you get the SQL Server error, the planted data it broke on, and a T-SQL repair hint.

**Target:** EF Core 10 / .NET 10, SQL Server. Your test project references `Microsoft.EntityFrameworkCore.SqlServer`, as your app does.

## What it plants

| When a migration… | it plants |
|---|---|
| adds a unique index, unique constraint or primary key | two rows with the same key (including EF's `IS NOT NULL` filtered indexes) |
| adds a foreign key | a row pointing at a principal that does not exist |
| makes a column NOT NULL | a NULL |
| shrinks a string or binary column | a value one longer than the new maximum |
| changes a column's type | a value the new type cannot hold: text in a column becoming a number, GUID or date; `32768` in one becoming `smallint`; `10^14` in a `decimal(18,2)` becoming `decimal(18,4)`; a year-1000 date in one becoming `datetime` |
| adds a NOT NULL column without a default | an existing row |

Every planted row is valid before the migration: foreign keys point at real rows, existing unique keys stay unique, values fit the old column. So a failure points at the migration, not at the test data. The data works across schemas, identity, rowversion, computed and GUID-keyed tables.

## Quick start

```csharp
var options = new MigrationTestOptions
{
    CreateContext = () => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
        .UseSqlServer("Server=localhost;Database=MyApp;Integrated Security=True;TrustServerCertificate=True")
        .Options)
};

var report = await MigrationTestRunner.RunAllAsync(options);
report.ThrowIfFailed();
```

`ThrowIfFailed()` throws a `MigrationTestException` whose message is the full report, so any test framework shows it:

```text
FAILED 20240301090000_AddOrderStatus: Up() failed on data that is valid before it runs.
SQL Server error 4901: ALTER TABLE only allows columns to be added that can contain nulls, or have a DEFAULT definition specified, ...

Broke on:
 - [NotNullColumnAddedWithoutDefault] NOT NULL column 'Status' added to 'Orders' without a default value

Repair hint:
-- Column [Status] is added as NOT NULL without a default value.
-- Either add a DEFAULT in the migration, or add it as nullable, UPDATE it, then make it NOT NULL.
```

### One test per migration

```csharp
public static TheoryData<string> Migrations => new(MigrationTestRunner.GetMigrationIds(Options.CreateContext));

[Theory]
[MemberData(nameof(Migrations))]
public async Task Migration_handles_existing_data(string migrationId)
{
    var step = await MigrationTestRunner.RunAsync(Options, migrationId);
    step.ThrowIfFailed();
}
```

## The test database

The runner drops and recreates its database for every migration, so by default it never touches the database in your connection string. Each run creates a throwaway database named after it (`MyApp_mt_1a2b3c4d`) and drops it when the run ends. The connection string only needs a server and a login that can create and drop databases.

| Option | Effect |
|---|---|
| `DatabaseName` | Use this exact database instead of a throwaway one, e.g. on a server where creating databases is slow. It is dropped and recreated, so only name a database you are happy to lose. |
| `KeepDatabase` | Leave the database in place after the run to inspect the last migration's data; `report.DatabaseName` tells you which one. |

## Reading the results

`RunAllAsync` and `RunRangeAsync` return a `MigrationTestReport`; `RunAsync` returns one `MigrationStepResult`.

| Member | Meaning |
|---|---|
| `Outcome` | `Passed`, `Failed` (the migration threw on the planted data) or `Error` (the step could not be set up, e.g. an earlier migration fails on an empty database) |
| `Scenarios` | Every case derived from `Up()`, each with a `Status`: `Planted`, `NotApplicable` (it cannot fail, e.g. the table is new) or `NotSeeded` (it could fail but could not be planted; see `Reason`) |
| `FailingScenarios` | The planted cases the SQL Server error matches |
| `Summary` / `ToString()` | The readable text shown above |
| `Warnings` | E.g. `migrationBuilder.Sql(...)` present, or a table that got no row |

Set `RequireAllScenariosSeeded = true` to also fail migrations with `NotSeeded` cases, so nothing passes untested.

## Choosing migrations

| Call | Tests |
|---|---|
| `RunAllAsync(options)` | every migration, or those in `options.MigrationIds`, in EF apply order |
| `RunRangeAsync(options, from, to)` | an inclusive slice of apply order, e.g. to split a long history across CI jobs |
| `RunAsync(options, id)` | one migration; `id` can be the full id or the migration name |

## SQL Server in Docker

```bash
docker run -e "ACCEPT_EULA=Y" -e "MSSQL_SA_PASSWORD=Your_password1" -p 1433:1433 -d mcr.microsoft.com/mssql/server:2022-latest
```

Then use `Server=127.0.0.1,1433;User Id=sa;Password=Your_password1;TrustServerCertificate=True` as the connection string.

## Limitations

- **SQL Server only.** Other EF Core providers need their own catalog queries, seed SQL and error handling.
- **Only `Up()` is tested**, and only operations built with `MigrationBuilder`; what `migrationBuilder.Sql(...)` does is not analyzed (flagged with a warning and `ContainsRawSql`).
- **Deterministic cases, not random data.** A migration can still fail on data nobody thought of.
- **Seeding** inserts one ordinary row per table and derives the bad rows from it. When SQL Server rejects a seed statement (an existing CHECK constraint, a trigger, a column type with no generator, a required self-referencing foreign key), those cases become `NotSeeded` with the reason, and the run carries on. Filtered unique indexes are seeded only when the filter is `IS NOT NULL` terms; new CHECK constraints are never seeded.
- **Silent data loss** (e.g. `datetime2` → `date`, scale reduction, `nvarchar` → `varchar`) does not fail a migration, so it is not reported.
- Every migration is tested from an empty database, so a long history means many migration runs.

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

Integration tests need SQL Server. The defaults in `TestSqlServerConfig` use Windows auth against the default local instance; override them with `MIGRATION_TESTER_SQL_TEST` (tests) and `MIGRATION_TESTER_SQL_DESIGN` (`dotnet ef`). For a SQL Express named instance, for example:

```bash
export MIGRATION_TESTER_SQL_TEST='Server=localhost\SQLEXPRESS;Database=MigrationTester_Test;Integrated Security=True;TrustServerCertificate=True'
```

Tests run in throwaway databases named after that one (`MigrationTester_Test_mt_…`). Before the integration tests start, any left behind by an interrupted run are dropped.

**Schema cases.** `tests/MigrationTester.Tests/SchemaCases/` runs end to end over hand-written migrations for common schema shapes (rowversion columns, GUID keys, non-`dbo` schemas, required foreign keys, filtered unique indexes, type narrowing, seed rows rejected by a CHECK constraint, a broken earlier migration). When you find a schema MigrationTester gets wrong, add it there as a test of the behavior you want and mark it `Explicit` until it is fixed; the default run skips explicit tests. Run them with:

```bash
dotnet test tests/MigrationTester.Tests -- xUnit.Explicit=only
```

The test project holds several `DbContext` types, so pass `--context TestDbContext` to `dotnet ef`.

**CI** (`.github/workflows/ci.yml`) runs the tests against a SQL Server container, packs the library, and builds the examples against the packed nupkg.

`dotnet-tools.json` pins dotnet-ef 10.0.13 for the test and sample migrations (`dotnet tool restore`).
