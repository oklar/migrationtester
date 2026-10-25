# Changelog

All notable changes to MigrationTester. Versions follow [Semantic Versioning](https://semver.org); before 1.0 any release may break the API.

## [Unreleased]

### Added

- New CHECK constraints get planted data when they compare one column with literals: `=`, `<>`, `!=`, `<`, `<=`, `>`, `>=`, `IN (...)` and `IS NOT NULL`, joined by `AND`, as in `[Price] > 0`, `[Status] IN (N'Open', N'Closed')` or `[Quantity] >= 1 AND [Quantity] <= 100`. One row gets a value that the column accepts before the migration but the constraint rejects, such as `0`, `N'x'` or a NULL. Other predicates, such as `LEN([Code]) = 3` or `[Start] < [End]`, are still `NotSeeded`.
- The repair hint for a CHECK constraint includes a query for the rows that fail its predicate.

### Changed

- Migrations that add such a CHECK constraint without handling existing rows that break it now fail; before, they passed untested. The sample's migration 09 (`[Price] > 0`) now fails on purpose.
- `FailingScenarios` matches a FOREIGN KEY or CHECK conflict (SQL Server error 547) only to the scenario planted for the constraint the error names.

### Fixed

- Operations that use a name given by a `RenameColumn` or `RenameTable` earlier in the same `Up()` now get planted data, instead of being reported as `NotSeeded` because the new name does not exist at N-1. EF scaffolds this for a renamed column that becomes NOT NULL or gets a unique index, for example. Renames are followed in order, through chains and moves to another schema; the scenario's `Table` and `Columns` hold the N-1 names, its `Name` mentions the rename, and `FailingScenarios` matches SQL Server errors that use the new names.

## [0.1.0-preview.1] - 2026-10-24

First preview release.

### Added

- `MigrationTestRunner.RunAllAsync`, `RunRangeAsync`, `RunAsync` and `GetMigrationIds` for testing EF Core migrations against SQL Server, as one run or one test case per migration.
- Planted data for every migration, valid before it runs:
  - duplicates for new unique indexes, unique constraints and primary keys, including `IS NOT NULL` filtered indexes;
  - orphan rows for new foreign keys;
  - NULLs for columns becoming NOT NULL;
  - over-long values for shrinking columns;
  - unconvertible values for type changes, including numeric narrowing, decimal precision or scale changes and `datetime2` → `datetime`;
  - existing rows for NOT NULL columns added without a default.
- Works across schemas and with identity, rowversion, computed, temporal and GUID-keyed tables; seeded keys avoid the ids HasData rows use.
- `MigrationTestReport` and `MigrationStepResult`:
  - per-step `Outcome` (`Passed`, `Failed`, `Error`);
  - per-scenario `Status` (`Planted`, `NotApplicable`, `NotSeeded`) with a reason;
  - `FailingScenarios` matched from SQL Server error numbers;
  - T-SQL repair hints;
  - `ThrowIfFailed()`, which throws with a readable report.
- A throwaway test database per run, dropped afterwards; `DatabaseName` and `KeepDatabase` to opt out.
- Each migration runs on its planted data in a transaction that is rolled back, so a run costs about two migration runs per migration; `RebuildDatabaseForEachMigration` recreates the database for every migration instead.
- `RequireAllScenariosSeeded` to fail migrations whose scenarios could not all be planted.

[Unreleased]: https://github.com/oklar/migrationtester/compare/v0.1.0-preview.1...HEAD
[0.1.0-preview.1]: https://github.com/oklar/migrationtester/releases/tag/v0.1.0-preview.1
