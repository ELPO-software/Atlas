# Architecture

## Projects

| Project | Responsibility | May reference |
|---|---|---|
| `Atlas.Core` | Models, business rules, validation, interfaces | Nothing |
| `Atlas.Infrastructure` | SQLite, migrations, logging, data paths | `Atlas.Core` |
| `Atlas.Platform` | Operating system integrations | `Atlas.Core` |
| `Atlas.App` | Avalonia views, view models, composition | All of the above |

`ArchitectureTests` fails the test run if one of these rules is broken.

Only `Atlas.App` knows Avalonia and only `Atlas.Infrastructure` knows SQLite. `AppComposition` is the one place that connects interfaces to implementations.

## Storage

Atlas keeps one SQLite file per user, in `%LOCALAPPDATA%\ELPO\Atlas\atlas.db`.

- **Dates** are stored as UTC text in the round-trip format, so sorting the text sorts the dates.
- **Writes** run in a transaction: they apply completely or not at all.
- **Threading:** `Microsoft.Data.Sqlite` completes its asynchronous methods synchronously, so `SqliteDatabase.RunAsync` moves database work to the thread pool to keep the interface responsive.

### Migrations

Schema changes are numbered scripts in `src/Atlas.Infrastructure/Persistence/Migrations`, named `0001_name.sql`. Versions start at 1 and have no gaps. To change the schema, add a new script; never edit one that has shipped.

`DatabaseMigrator` runs at startup. It:

1. Refuses a file that belongs to another program, a file it cannot read, and a database from a newer version of Atlas. These files are left untouched.
2. Copies existing data to `atlas.db.v<version>.<timestamp>.bak` before any upgrade.
3. Applies each pending script in its own transaction, so a failed upgrade leaves the data as it was.

Atlas never repairs, replaces or deletes a data file on its own. A failure at startup becomes a message for the user.

## Logs

Diagnostic logs stay on the computer, in `%LOCALAPPDATA%\ELPO\Atlas\logs`. A new file starts each day or at 5 MB, and Atlas keeps the 14 most recent files. These are the only files Atlas deletes on its own.

## Adding a resource type

A resource type is a text value (`ResourceKind`). Adding one does not change the database schema. Values Atlas does not know are preserved, so they can be reported and never silently lost.

## Conventions

- **Interface text** lives in `src/Atlas.App/Resources/Strings.resx`, never in views or view models. French is the neutral language.
- **`Atlas.Core`** contains no user-facing text. It reports problems as codes, which the interface turns into messages.
- **Package versions** are declared once, in `Directory.Packages.props`.

## Tool telemetry

Two libraries bring telemetry into a normal build or test run:

| Package | Pulled in by | When it reports | Excluded in |
|---|---|---|---|
| `Avalonia.BuildServices` | `Avalonia` | During builds | `Directory.Build.props` |
| `Microsoft.Testing.Extensions.Telemetry` | `xunit.v3` | During test runs | `tests/Directory.Build.props` |

The `AtlasRefuseToolTelemetry` target in `Directory.Build.targets` fails the build if either exclusion stops working, and `ArchitectureTests` checks that no telemetry library reaches the test output. `Microsoft.ApplicationInsights`, which the test extension sends its data through, is excluded with it.

## xUnit version

`xunit.v3` is pinned to 3.2.2 because `Avalonia.Headless.XUnit` 12.1.5 is built against that version. Upgrade them together, along with the excluded telemetry package, whose version follows `xunit.v3`.
