# Security

## Reporting a vulnerability

Please do not describe a vulnerability in a public issue. Report it privately instead: on the [Security tab](https://github.com/ELPO-software/Atlas/security) of the repository, choose **Report a vulnerability**. You will get an answer, and credit if you wish, once the fix is published.

Atlas is a beta. Only the latest release receives fixes.

## What Atlas does and does not do

Atlas opens files, folders, links and programs on request, and it can observe the session to propose environments, so the rules below are part of its design. A way around any of them is a vulnerability.

- **It works offline.** Atlas has no network code and sends nothing. A test fails if Atlas code references a networking library. Opening a link hands it to the default browser. There is no automatic update.
- **It never runs code from a file.** Files that would run code when opened, such as scripts, shortcuts and installers, are refused as files, including names Windows reads the same way, like `script.bat.`. Windows' own list of dangerous types is checked as well. Programs are added as applications, and they start without any command-line argument.
- **It refuses ambiguous locations:** incomplete paths, device paths, wildcards, alternate data streams such as `notes.txt:hidden.exe`, and links other than `http` and `https`. Environment variables are never expanded.
- **It asks before starting several programs,** and it never closes anything.
- **Imports are inert.** An imported file is read, never followed: during an import no path is looked up and no server is contacted. The reader refuses files over 2 MB, deep nesting, repeated or unknown properties and more than 1,000 resources. Imported applications and network locations are not opened until the user approves each one.
- **It never deletes the user's files.** Deleting a workspace deletes what Atlas stored about it, and the confirmation says so. The only files Atlas removes on its own are its old log files.

## Environment detection

The detection is off until the user allows it, and the user can turn it off or make Atlas forget what it observed at any time.

- **It only reads.** Atlas lists the windows of the user's session, the folders open in File Explorer, and for each program with a window, its path and the files and folders it was started with. It reads the settings files in which Visual Studio Code and Visual Studio list their recent folders and solutions, and the Git configuration of a repository to find its web address. It never opens, starts, closes or changes anything, and it does not read the content of the user's files.
- **It keeps what it needs, nothing more.** A command line is read, kept in memory only to find the existing files and folders it names, and dropped: arguments that are not paths, such as options or tokens, are never stored. Locations inside Windows, Program Files, ProgramData, the temporary folder, the application data folders and the program's own folder are left out. Credentials written in a Git remote address are removed before the address can reach a proposal.
- **It stays on the computer.** Proposals and decisions are stored in the database of Atlas, and the logs never record the paths or programs the detection sees.
- **It saves nothing on its own.** A proposal becomes a workspace only when the user chooses to save it, and every resource is checked by the same rules as one added by hand. A proposal Atlas is not sure about is marked as probable and is never saved from a notification: the user reviews it first.
- **It cannot be steered from outside.** A notification only carries a short tag and the choice of the user; Atlas acts on it only for a proposal it announced itself, while it runs.

## Notifications

Only the installed copy of Atlas, whose Start menu entry carries its identity, shows Windows notifications; a portable copy shows its proposals inside its own window. Atlas removes its notifications when it closes. Uninstalling removes the notification settings Windows keeps for Atlas.

## Where data lives

Atlas keeps its database and logs in `%LOCALAPPDATA%\ELPO\Atlas`, readable by the Windows account that runs it. The database is not encrypted: anyone who can read that folder can read the workspaces, the notes and the proposed environments. Exported files contain the locations of the resources, which often reveal the Windows user name; Atlas warns before exporting them.

The installer writes only the program folder, the Start menu entry, an optional desktop shortcut and the uninstall information Windows needs. It never touches the data folder, so updating and uninstalling keep the data.

## Distribution

The installers and archives are not code-signed yet, so Windows SmartScreen warns before the first start. Each release lists the SHA-256 checksum of every download in `SHA256SUMS.txt`; compare it before running Atlas. The downloads of a release are built and tested by the [CI workflow](.github/workflows/ci.yml) of the tagged commit.
