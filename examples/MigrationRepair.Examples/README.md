# MigrationRepair.Examples

Consumer sample under **`examples/MigrationRepair.Examples`** in the MigrationRepair repo. References the **MigrationRepair** NuGet package built from **`../../bin/Release`** (two levels up from this folder to the library project root).

## Prerequisites

- .NET 10 SDK  
- SQL Server (local, Docker, or remote) — same as the main library README  
- Packed library (from the `MigrationRepair` directory that contains `MigrationRepair.csproj`):

  ```bash
  dotnet pack ../../MigrationRepair.csproj -c Release
  ```

## Configuration

| Env var | Purpose |
|--------|---------|
| `MIGRATION_REPAIR_SCENARIO_TEST` | Connection string for integration tests (DB is dropped at test collection start). |
| `MIGRATION_REPAIR_SCENARIO_DESIGN` | Connection string for `dotnet ef` design-time. |

Defaults use **Windows auth** and databases `MigrationRepairExamples_Test` / `MigrationRepairExamples_Design`.

## Migrations

There are **9** EF Core migrations (01–06 build the schema; **07** check constraint on `Products.Code`; **08** optional `Orders.Status`; **09** unique index **`IX_Products_Code`**). The adversary targets that last step. (A separate **column length shrink** migration was omitted so the full chain fails only on uniqueness—shrinks are also adversary-hostile.)

## Commands

Working directory: this folder (`examples/MigrationRepair.Examples`).

Restore (local `.nupkg` from `../../bin/Release`):

```bash
dotnet restore
```

EF tools:

```bash
dotnet tool restore
```

Run tests (**they fail on purpose** — red status so the test message carries `FailureSummary` and SQL hints):

```bash
dotnet test --logger "console;verbosity=detailed"
```

Filter:

```bash
dotnet test --filter FullyQualifiedName~AdversaryFailureDemoTests
```

## Re-pack after library changes

Pack **`MigrationRepair.csproj`** again and restore this project so tests use the new package.

## NuGet package size

Only `MigrationRepair.csproj` is packed; this example project is **not** included in the `.nupkg` unless you explicitly add packed files.
