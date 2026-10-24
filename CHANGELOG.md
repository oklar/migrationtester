# Changelog

All notable changes to MigrationTester. Versions follow [Semantic Versioning](https://semver.org); before 1.0 any release may break the API.

## [Unreleased]

First release, planned as `0.1.0-preview.1`.

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
