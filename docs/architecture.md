# Architecture

## Projects

| Project | Responsibility | May reference |
|---|---|---|
| `Atlas.Core` | Models, business rules, validation, interfaces | Nothing |
| `Atlas.Infrastructure` | SQLite, migrations, logging, data paths | `Atlas.Core` |
| `Atlas.Platform` | Operating system integrations: opening resources, reading the session, notifications | `Atlas.Core` |
| `Atlas.App` | Avalonia views, view models, composition | All of the above |

`ArchitectureTests` fails the test run if one of these rules is broken.

Only `Atlas.App` knows Avalonia and only `Atlas.Infrastructure` knows SQLite. `AppComposition` is the one place that connects interfaces to implementations.

## Storage

Atlas keeps one SQLite file per user, in `%LOCALAPPDATA%\ELPO\Atlas\atlas.db`. The environment variable `ATLAS_DATA_DIR` names another folder, as a full path; tests and trials use it to stay away from real data.

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

### Versions and history

- **Versions** keep the whole state of a workspace in the format of [file-format.md](file-format.md). Going back to a version first saves the current state as an automatic version, in its own transaction: if going back fails, nothing is lost. Versions are deleted with their workspace, which the deletion confirmation says.
- **History** lines are written in the same transaction as the change they describe, so a change and its line apply together or not at all. The history has no foreign key and outlives deleted workspaces. Notes saved within 30 minutes of each other share one line. Lines written by a newer version stay in the file and are not shown.

### Concurrent changes

A change reads a workspace and writes it back whole. `WorkspaceService` runs changes one at a time, so the automatic save of the notes cannot overwrite a rename that lands between its read and its write. The single-instance guard means no other process writes to the file.

## Logs

Diagnostic logs stay on the computer, in `%LOCALAPPDATA%\ELPO\Atlas\logs`. A new file starts each day or at 5 MB, and Atlas keeps the 14 most recent files. These are the only files Atlas deletes on its own.

## Interface

`MainWindowViewModel` owns the current page, the banner that announces a suggestion, and the count of suggestions next to the navigation, and implements `INavigator`. Each page is a `PageViewModel`, created for each visit, and `MainWindow` picks its view through data templates.

- **Leaving a page:** before a page is replaced or the window closes, `TryLeaveAsync` lets it finish its work. A page can refuse, which keeps the user on it. `WorkspaceDetailViewModel` uses this to store pending notes, or to ask before losing notes it cannot store.
- **Notes** are saved one second after typing stops. Saves run one at a time, and text typed during a save is stored right after it. A failed save is reported, and the text stays on screen.
- **Dialogs** go through `IDialogService`, so view models can be tested without windows. A form stays open until its save succeeds, so a refused value or a failed save loses nothing. A confirmation starts on the safe answer.
- **Failures:** pages report the failures they expect, with the location of the log. `AtlasApp` logs anything else and shows a banner.
- **Text** is formatted in French by `UiText` and `DateText`, whatever the language of the system, because the interface is written in French.

### Theme

`Styles/Theme.axaml` is the only place that defines colors and icons, and `Styles/Controls.axaml` holds the text and control styles: panels, chips, information bars (information, success, warning, error), empty states, thin progress lines, icon tiles for each type of resource (`ResourceIcons`) and the footer band of dialogs. Blue is reserved for the main action of a screen, the navigation selection and what Atlas wants the user to notice. Surfaces are separated by their fill; a hairline border only marks the edge of panels and of the sidebar. A short fade separates pages, unless the user turned animations off in Windows (`SystemMotion`).

`ThemeTests` reads the colors from the real theme and fails if text drops below 4.5:1 or a control boundary below 3:1. Add any new pair of colors to it.

The title bar stays the one drawn by the system, which keeps its buttons and window snapping. `WindowChrome` tints it to match the page where the system allows it, which means Windows 11. Extending the page under the title bar is not an option: Avalonia then draws its own buttons.

### Third-party components

`ThirdPartyComponents` lists what ships with Atlas, shown in the settings. `ThirdPartyComponentsTests` fails if a package in the application has no entry. Licenses come from each package's own metadata, except for the Inter font, whose OFL-1.1 license is separate from the MIT license of the package that carries it.

## Restoration

A restoration has two parts, kept apart so that the decisions can be tested without opening anything.

- **`RestorePlan`** (Core) decides before anything opens: applications first, then files and folders, then links. It skips, with a reason, resources waiting for approval, files that would run code, targets that break the rules and types Atlas does not know. A resource waiting for approval is skipped before its path is even looked at, so a network path from an imported file never contacts its server.
- **`RestorationService`** (Core) opens the plan's resources one after the other. A resource that fails never stops the others. Each one has 15 seconds to answer; past that, the report says Windows did not answer and the restoration moves on. Stopping takes effect before the next resource, so every result in the report is true.
- **`IResourceOpener`** (Platform) does the opening. On Windows, `WindowsResourceOpener` checks that the resource is there, then asks the system to open it as a double-click would. It never passes command-line arguments. Programs that stop with an error within 1.5 seconds are reported as failed; all programs get those 1.5 seconds at the same time, at the end.

The user confirms before two or more applications start, with each one listed. Atlas never closes anything.

"Restored" means that Windows accepted to open the resource. For a packaged application, Windows does not even report a process to watch. The report says so.

### What Atlas refuses to open

- **Files that run code** (`ResourceTargets.RunsCode`): executables, scripts, shortcuts, installers and the like, including names Windows would read the same way, such as `lancer.bat.`. Windows' own list of dangerous types (`AssocIsDangerous`) is checked as well when opening. Programs are added as applications instead.
- **Paths that Windows would read differently:** relative paths, device paths (`\\?\`, `\\.\`), wildcards and alternate data streams (`notes.txt:hidden.exe`).
- **Links other than `http` and `https`**, such as `file:`, `javascript:` or `ms-settings:`.
- **Applications that are not programs:** an application is either a `.exe` file or an entry of the Windows application list (`shell:AppsFolder`). Entries of that list that are web shortcuts or documents are left out.

A folder is opened with a final separator, so that a file with the same name can never answer instead.

### Other systems

Only Windows can open resources for now. Elsewhere, `UnsupportedResourceOpener` reports every resource as failed instead of pretending.

## Import and export

`WorkspaceDocument` writes and reads the file format; its reader refuses anything unexpected and never throws for bad input. `ImportPlan` decides from the file alone what each resource becomes: imported, held for approval, kept as an unknown type, or left out with the rule it breaks. `WorkspaceFiles` refuses a file larger than 2 MB from its size, before reading it, and writes exports to a temporary file that then replaces the destination. [file-format.md](file-format.md) describes the format and the rules.

## Adding a resource type

A resource type is a text value (`ResourceKind`). Adding one does not change the database schema: it needs its rules in `ResourceRules`, its place in `RestorePlan` and its opener. Values Atlas does not know are preserved, so they can be reported and never silently lost.

## Conventions

- **Interface text** lives in `src/Atlas.App/Resources/Strings.resx`, never in views or view models. French is the neutral language.
- **`Atlas.Core`** contains no user-facing text. It reports problems as codes, which the interface turns into messages.
- **Package versions** are declared once, in `Directory.Packages.props`.

## Environment detection

The detection proposes workspaces from what the user has open. It is split like the restoration, so that every decision can be tested without reading the real session.

- **`IWorkObserver`** (Core) reads the session into a `WorkObservation`: the programs with a window and what they have open, the folders open in File Explorer, and the projects behind them. On Windows, `WindowsWorkObserver` (Platform) lists the windows of the session, reads each program's path and command line (`WindowsSession`), asks the shell for its File Explorer windows (`ExplorerWindows`), reads the recent folders of Visual Studio Code editors and the recent solutions of Visual Studio and keeps those whose name appears in a window title (`OpenItems`), and walks up from each location to the Git repository, solution or package manifest that holds it (`ProjectLocator`). It only reads, keeps paths that exist and leaves out system and program folders.
- **`EnvironmentDetector`** (Core) turns an observation into `EnvironmentProposal`s: one per project, and one recurring context for what belongs to no project. It decides how sure it is: a project that a program has open is detected, a project only seen in File Explorer and every context are probable. Every resource goes through `ResourceRules`, so a proposal never holds what Atlas would refuse.
- **`EnvironmentDetectionService`** (Core) keeps proposals and decisions through `IDetectionStore`, and decides what to show and what to announce: a context only once seen on two days, a project only once still there after 90 seconds, at most one announcement every 20 minutes, never twice the same, and never what a workspace already holds. "Later" lasts 8 hours; proposals nobody decided about are forgotten after 30 days. Saving goes through `WorkspaceService.CreateFromDetectionAsync`, with its own line of history.
- **`SuggestionCenter`** (App) runs a scan every two minutes while Atlas is open and the user allowed it, and announces a proposal through `IDesktopNotifications` when Atlas is in the background, or through the banner of the main window. It applies the choice made in a notification, and removes every notification when Atlas closes.

The settings `detection` (null until the user decides, then `on` or `off`) and `detection-notifications` are stored with the other settings. Proposals live in the `detected_environments` table (migration `0004_detection.sql`), with their resources as JSON.

The shell's scripting objects (Shell.Application) are only used on `ShellApartment`, one long-lived single-threaded apartment: the shell may hand the same object to several threads, and a thread that ends would detach it for the others.

## Notifications

`WindowsNotifications` (Platform) shows Windows notifications through the Windows Runtime, declared by hand in `WinRtInterop.cs` from the Windows metadata, so that Atlas needs no extra library. The identifier of the activation handler is computed as the Windows Runtime specifies, and a test recomputes it.

Windows attributes a notification to the Start menu entry that carries the application identity `ELPO.Atlas`, which the installer creates. `InstalledCopy` compares the folder of the running Atlas with the one the installer recorded: only the installed copy notifies and adopts that identity, at startup, before its first window. Windows keeps an identity it has seen, even after an uninstallation, so its mere availability does not prove anything.

## Starting twice

`SingleInstanceGuard` holds a name per data folder, and a name every running Atlas holds, which the installer waits for. A second start of Atlas signals the first one through a named event, hands it the right to come to the foreground, and ends. The first one restores its window if it was minimized and brings it forward.

## Release

`build/publish.ps1` publishes Atlas with its own .NET runtime for x64 and for ARM64, checks that every binary of each copy is built for its processor, and writes to `artifacts`:

- `Atlas-Setup-x64.exe` and `Atlas-Setup-ARM64.exe`, built by Inno Setup 6.7 from `build/installer/Atlas.iss`;
- `Atlas-Portable-x64.zip` and `Atlas-Portable-ARM64.zip`;
- `SHA256SUMS.txt`.

Each copy carries `LICENSE`, `THIRD-PARTY-NOTICES.md` and the `licenses` folder, whose texts are also built into Atlas and shown from the settings. The native debug symbols that SkiaSharp and HarfBuzz add to every publish, about 100 MB, are left out by the `AtlasLeaveSymbolsOutOfPublish` target of `Atlas.App.csproj`.

The installer has one identity for both processors, so Windows holds one Atlas at a time. It installs for all users or for the current one, keeps that choice for updates, refuses to install on the other processor, waits while Atlas runs, asks before replacing a newer version, and never touches the data folder. Uninstalling also removes the notification settings Windows keeps for Atlas.

`build/test-installer.ps1` installs, starts, updates and uninstalls an installer and checks what Windows shows at each step; `build/test-portable.ps1` checks a portable archive. Both refuse to run where Atlas is already installed or running, and accept a separate data folder.

The CI workflow (`.github/workflows/ci.yml`) builds and tests on `windows-2025` (x64) and `windows-11-arm` (ARM64), builds the downloads with a pinned and checked Inno Setup, then runs both scripts on a computer of each processor, for all users and for one user, and checks that the installer of the other processor refuses to install. The downloads of a release are the ones this workflow built and tested.

`ReleaseTests` checks that no Atlas assembly references a networking library, and that `THIRD-PARTY-NOTICES.md` names every component shown in the settings and links only to license files that exist.

## Tool telemetry

Two libraries bring telemetry into a normal build or test run:

| Package | Pulled in by | When it reports | Excluded in |
|---|---|---|---|
| `Avalonia.BuildServices` | `Avalonia` | During builds | `Directory.Build.props` |
| `Microsoft.Testing.Extensions.Telemetry` | `xunit.v3` | During test runs | `tests/Directory.Build.props` |

The `AtlasRefuseToolTelemetry` target in `Directory.Build.targets` fails the build if either exclusion stops working, and `ArchitectureTests` checks that no telemetry library reaches the test output. `Microsoft.ApplicationInsights`, which the test extension sends its data through, is excluded with it.

## xUnit version

`xunit.v3` is pinned to 3.2.2 because `Avalonia.Headless.XUnit` 12.1.6 is built against that version. Upgrade them together, along with the excluded telemetry package, whose version follows `xunit.v3`.
