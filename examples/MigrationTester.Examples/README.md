# MigrationTester.Examples

A consumer sample: an EF Core model with nine SQL Server migrations, and tests that run the adversary over them. It builds against the library source by default, so there is nothing to pack first.

## Prerequisites

- .NET 10 SDK
- SQL Server (local, Docker, or remote) and a login that can create and drop databases

## Configuration

| Env var | Purpose |
|--------|---------|
| `MIGRATION_TESTER_SCENARIO_TEST` | Connection string for the tests. The database is dropped when the test collection starts. |
| `MIGRATION_TESTER_SCENARIO_DESIGN` | Connection string for `dotnet ef` design time. |

Defaults use **Windows auth** on the default local instance, with databases `MigrationTesterExamples_Test` / `MigrationTesterExamples_Design`.

## Migrations

| # | Migration | What the adversary plants |
|---|-----------|---------------------------|
| 01 | `InitialSchema` | Nothing; the first migration has no earlier data, so it passes |
| 02 | `AddForeignKeyOrdersCustomers` | An `Orders` row whose `CustomerId` matches no customer |
| 03 | `ShrinkProductCodeLength` | A `Products.Code` longer than the new max length |
| 04 | `AddUniqueProductCode` | Two products with the same `Code` |
| 05 | `MakeProductNameRequired` | A product with a NULL `Name` |
| 06 | `AddOrderStatus` | Existing `Orders` rows, which a NOT NULL column without a default cannot fill |
| 07 | `ChangeTagExternalIdToGuid` | A `Tags.ExternalId` that is not a GUID |
| 08 | `ShrinkProductPricePrecision` | A `Products.Price` too large for `decimal(10,2)` |
| 09 | `AddCheckConstraintPositivePrice` | Nothing; CHECK constraints are not seeded automatically, so it passes |

## Commands

From the repository root:

```bash
dotnet test examples/MigrationTester.Examples
```

`RunAllAsync_each_migration_triggers_its_adversarial_scenario` asserts that 02–08 fail and 01 and 09 pass, so it is green.

`RunAsync_single_migration_shows_failure_summary_and_hints` fails on purpose so the test output shows the full failure summary and T-SQL repair hints. It is `Explicit`, so run it by asking for it:

```bash
dotnet test examples/MigrationTester.Examples --logger "console;verbosity=detailed" -- xUnit.Explicit=only
```

EF tools (`dotnet tool restore` once, from the repository root):

```bash
dotnet ef migrations list --project examples/MigrationTester.Examples
```

## Against the packed package

To build the sample the way a NuGet consumer would:

```bash
dotnet pack src/MigrationTester -c Release
dotnet build examples/MigrationTester.Examples -p:UseMigrationTesterPackage=true
```

Package mode restores from `artifacts/packages` into `artifacts/examples-packages` instead of the global NuGet cache, so a locally packed version never shadows a published one. After repacking the same version, delete `artifacts/examples-packages` to pick up the new build. CI runs this on every push.
