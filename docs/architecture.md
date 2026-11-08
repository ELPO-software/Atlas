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

### Settings

Preferences are named text values in the `settings` table. `SettingsService` turns them into typed values. A value it does not recognize, such as one written by a newer version, falls back to the default and stays in the file until the user changes that setting.

### Concurrent changes

A change reads a workspace and writes it back whole. `WorkspaceService` runs changes one at a time, so the automatic save of the notes cannot overwrite a rename that lands between its read and its write. The single-instance guard means no other process writes to the file.

## Logs

Diagnostic logs stay on the computer, in `%LOCALAPPDATA%\ELPO\Atlas\logs`. A new file starts each day or at 5 MB, and Atlas keeps the 14 most recent files. These are the only files Atlas deletes on its own.

## Interface

`MainWindowViewModel` owns the current page and implements `INavigator`. Each page is a `PageViewModel`, created for each visit, and `MainWindow` picks its view through data templates.

- **Leaving a page:** before a page is replaced or the window closes, `TryLeaveAsync` lets it finish its work. A page can refuse, which keeps the user on it. `WorkspaceDetailViewModel` uses this to store pending notes, or to ask before losing notes it cannot store.
- **Notes** are saved one second after typing stops. Saves run one at a time, and text typed during a save is stored right after it. A failed save is reported, and the text stays on screen.
- **Dialogs** go through `IDialogService`, so view models can be tested without windows. A form stays open until its save succeeds, so a refused value or a failed save loses nothing. A confirmation starts on the safe answer.
- **Failures:** pages report the failures they expect, with the location of the log. `AtlasApp` logs anything else and shows a banner.
- **Text** is formatted in French by `UiText` and `DateText`, whatever the language of the system, because the interface is written in French.

### Theme

`Styles/Theme.axaml` is the only place that defines colors, and `Styles/Controls.axaml` holds the text and control styles. Blue is reserved for the main action of a screen and the navigation selection. Surfaces are separated by their fill, not by borders.

`ThemeTests` reads the colors from the real theme and fails if text drops below 4.5:1 or a control boundary below 3:1. Add any new pair of colors to it.

The title bar stays the one drawn by the system, which keeps its buttons and window snapping. `WindowChrome` tints it to match the page where the system allows it, which means Windows 11. Extending the page under the title bar is not an option: Avalonia then draws its own buttons.

### Third-party components

`ThirdPartyComponents` lists what ships with Atlas, shown in the settings. `ThirdPartyComponentsTests` fails if a package in the application has no entry. Licenses come from each package's own metadata, except for the Inter font, whose OFL-1.1 license is separate from the MIT license of the package that carries it.

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
