# MigrationTester

[![CI](https://github.com/oklar/migrationtester/actions/workflows/ci.yml/badge.svg)](https://github.com/oklar/migrationtester/actions/workflows/ci.yml)
[![NuGet](https://img.shields.io/nuget/vpre/MigrationTester)](https://www.nuget.org/packages/MigrationTester)

Tests EF Core migrations against SQL Server with the kind of data production already has. For each migration N, MigrationTester brings a test database to N−1, plants rows that are valid there but that N's `Up()` may not handle, then runs N. When the migration fails you get the SQL Server error, the planted data it broke on, and a T-SQL repair hint.

**Target:** EF Core 10 / .NET 10, SQL Server. Your test project references `Microsoft.EntityFrameworkCore.SqlServer`, as your app does.

```bash
dotnet add package MigrationTester --prerelease
```

## What it plants

| When a migration… | it plants |
|---|---|
| adds a unique index, unique constraint or primary key | two rows with the same key (including EF's `IS NOT NULL` filtered indexes) |
| adds a foreign key | a row pointing at a principal that does not exist |
| makes a column NOT NULL | a NULL |
| shrinks a string or binary column | a value one longer than the new maximum |
| changes a column's type | a value the new type cannot hold: text in a column becoming a number, GUID or date; `32768` in one becoming `smallint`; `10^14` in a `decimal(18,2)` becoming `decimal(18,4)`; a year-1000 date in one becoming `datetime` |
| adds a NOT NULL column without a default | an existing row |
| adds a CHECK constraint that compares one column with literals (`=`, `<>`, `<`, `<=`, `>`, `>=`, `IN (...)`, `IS NOT NULL`, joined by `AND`) | a value that breaks it: `0` for `[Price] > 0`, `N'x'` for `[Status] IN (N'Open', N'Closed')`, a NULL for `IS NOT NULL` |

Every planted row is valid before the migration: foreign keys point at real rows, existing unique keys stay unique, values fit the old column. So a failure points at the migration, not at the test data. The data works across schemas and with identity, rowversion, computed, temporal and GUID-keyed tables.

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

By default the runner never touches the database in your connection string. Each run creates a throwaway database named after it (`MyApp_mt_1a2b3c4d`) and drops it when the run ends. The connection string only needs a server and a login that can create and drop databases.

A run migrates that database forward once. For each migration it plants the data and runs the migration inside a transaction that is rolled back, then applies the migration to the clean database for the next step. That is about two migration runs per migration, with the same starting state as rebuilding from empty every time; on the sample's 9 migrations it takes 0.65 s instead of 5 s. Migrations with statements that cannot run in a transaction (`Sql(..., suppressTransaction: true)`, `ALTER DATABASE`) are planted and run for real, and the database is rebuilt afterwards.

| Option | Effect |
|---|---|
| `DatabaseName` | Use this exact database instead of a throwaway one, e.g. on a server where creating databases is slow. It is dropped and recreated, so only name a database you are happy to lose. |
| `KeepDatabase` | Leave the database in place after the run, holding the last migration's planted data and result; `report.DatabaseName` tells you which one. |
| `RebuildDatabaseForEachMigration` | Recreate the database from empty and plant data for real for every migration. Slower; for when you suspect the transactional run behaves differently. |

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
- **Seeding** inserts one ordinary row per table and derives the bad rows from it. When SQL Server rejects a seed statement (an existing CHECK constraint, a trigger, a column type with no generator, a required self-referencing foreign key), those cases become `NotSeeded` with the reason, and the run carries on. Filtered unique indexes are seeded only when the filter is `IS NOT NULL` terms.
- **New CHECK constraints** are seeded only when they compare one column with literals, as in the table above, and the column is a number (`int`, `decimal`, `money`, `float`, `bit` and the like) or a `char`/`varchar`/`nchar`/`nvarchar` string; for strings only `=`, `<>`, `<`, `>` and `IN` are used, since what sorts below or above a value depends on the collation. Anything else, such as `LEN([Code]) = 3`, `[Start] < [End]`, `OR`, or a date column, is reported as `NotSeeded`.
- **Renames are followed within one `Up()`.** Operations after a `RenameColumn` or `RenameTable` (including a move to another schema) are planted under the N-1 names, which the scenario's `Table` and `Columns` hold. A rename done another way, such as `sp_rename` in `migrationBuilder.Sql(...)`, is not followed.
- **Silent data loss** (e.g. `datetime2` → `date`, scale reduction, `nvarchar` → `varchar`) does not fail a migration, so it is not reported.
- **Seed values avoid HasData ids** by starting identity counters and integer keys at 1,000,000 (30,000 for `smallint`, 100 for `tinyint`). Seed data with ids above that can still collide.

## More

- [Changelog](https://github.com/oklar/migrationtester/blob/main/CHANGELOG.md)
- [Sample project](https://github.com/oklar/migrationtester/tree/main/examples/MigrationTester.Examples): nine migrations that each break on a different kind of data
- [Contributing](https://github.com/oklar/migrationtester/blob/main/CONTRIBUTING.md): building, testing and releasing
