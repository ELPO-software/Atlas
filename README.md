# Atlas

A local-first workspace manager to organize and restore your working environments.

Atlas is part of the ELPO software suite. It saves the applications, files, folders, links and notes of an activity, so you can bring them all back in a few clicks. It can also notice the projects and the sets of applications you work with, and offer to save them for you.

## Status

Atlas is a beta: version 0.2.0-beta.1. Its interface is in French. It runs on Windows 10 (version 1809 or later) and Windows 11, on x64 and ARM64 processors. It has been tested on Windows 11 x64, and on Windows 11 ARM64 by the automated build. Keep your own copies of what matters.

## What it does

- **Workspaces** gather the applications, files, folders and web links of an activity, with notes that save themselves as you type.
- **Restore** opens them all: applications first, then files and folders, then links. Atlas asks before starting several programs, never closes anything, and reports what opened, what was missing and what failed.
- **Suggestions.** Once you allow it, Atlas notices the projects you open in Visual Studio Code, Visual Studio or File Explorer, and the applications, folders and files you keep using together. It proposes them as workspaces, says whether it is sure or only guessing, and saves nothing until you choose **Enregistrer**. See [Environment detection](#environment-detection).
- **Versions** keep the state of a workspace, so you can go back to it. Going back first saves the current state.
- **History** lists what was done, including deleted workspaces.
- **Import and export** a workspace as a JSON file. An import shows what it contains before creating anything, and imported applications wait for your approval.
- **Themes:** light, dark, or following Windows. Page transitions follow the animation setting of Windows.

## System requirements

- Windows 10 version 1809 or later, or Windows 11.
- A 64-bit processor: x64 (Intel, AMD) or ARM64.
- About 150 MB of disk space. Nothing else: the downloads include the .NET runtime.

## Install

Each [release](https://github.com/ELPO-software/Atlas/releases) offers one installer per processor:

| Your computer | Download |
|---|---|
| Intel or AMD processor (x64) | `Atlas-Setup-x64.exe` |
| ARM processor, such as Snapdragon | `Atlas-Setup-ARM64.exe` |

To know which one you need, open **Paramètres > Système > Informations système**: the **Type du système** says "processeur x64" or "processeur ARM". Each installer refuses to install on the other kind of processor.

Run the installer and choose:

- **Installer pour tous les utilisateurs** (recommended), in `C:\Program Files\ELPO\Atlas`. Windows asks for administrator rights.
- **Installer seulement pour moi**, in `%LOCALAPPDATA%\Programs\ELPO\Atlas`, without administrator rights.

Atlas then appears in the Start menu as **ELPO Atlas**, and in **Paramètres > Applications > Applications installées** with its publisher, ELPO software. A desktop shortcut is offered, unchecked by default.

The installers are not code-signed yet. Windows SmartScreen may say it protected your PC: choose **Informations complémentaires**, then **Exécuter quand même**. To check a download, compare its SHA-256 checksum, from `Get-FileHash` in PowerShell, with the `SHA256SUMS.txt` file of the release.

Administrators can install silently: `Atlas-Setup-x64.exe /VERYSILENT /ALLUSERS`, or `/CURRENTUSER` for the current account only.

### Portable copy

The release also offers `Atlas-Portable-x64.zip` and `Atlas-Portable-ARM64.zip`: extract one anywhere and run `Atlas.exe`. A portable copy installs nothing and has no Start menu entry, so it shows its suggestions inside Atlas only, never as Windows notifications. It uses the same data as an installed Atlas.

## Update

Run the installer of the new version. It updates the installed Atlas in place, in the same folder and for the same users, and keeps your workspaces and settings, which live elsewhere (see [Your data](#your-data)). If Atlas is open, the installer asks you to close it first. Installing an older version over a newer one asks for confirmation, because the older version may not read the newer data.

## Uninstall

Open **Paramètres > Applications > Applications installées**, find **ELPO Atlas**, and choose **Désinstaller**. This removes the program, its Start menu entry and its notification settings.

Your data stays, so that a later installation finds it again. To remove it as well, delete the folder `%LOCALAPPDATA%\ELPO\Atlas` of each Windows account that used Atlas. For a portable copy, delete the extracted folder.

## Your data

Atlas keeps everything in `%LOCALAPPDATA%\ELPO\Atlas`, for each Windows account:

- `atlas.db`: the workspaces, their versions, the history, the settings, and the environments Atlas proposed with your decisions about them;
- `logs`: diagnostic logs, kept 14 days at most. The detection writes neither the paths nor the programs it sees to them;
- `atlas.db.v<version>.<date>.bak`: a copy made before Atlas upgrades the database to a new version.

Nothing leaves the computer. The settings page shows the folder and opens it.

## Environment detection

Atlas observes nothing until you allow it, from the home page, the **Suggestions** page or the settings.

Once allowed, Atlas looks every two minutes, while it is open, at:

- the programs that have a window, and the files and folders they were opened with;
- the folders open in File Explorer;
- the projects open in Visual Studio Code and its relatives (Cursor, VSCodium, Windsurf) or in Visual Studio, matched by their window titles;
- for each of those locations, the project that holds it: its Git repository, its solution, and the web address of its repository.

It does not read the content of your files, the pages open in your browser, or anything a program running as administrator holds.

From that, Atlas proposes:

- **projects:** the project folder, its solution, the tools that have it open, the files open in them, and its repository on the web. A project open in a tool is marked **Détecté**; a project only seen in File Explorer is **Probable · à vérifier**;
- **recurring contexts:** applications, folders and files open together on at least two different days, always marked **Probable · à vérifier**.

A proposal appears on the **Suggestions** page, where you can rename it, untick what you do not want, and choose **Enregistrer**, **Plus tard** or **Ignorer**. When Atlas is in the background, an installed Atlas also announces a new proposal with a Windows notification; otherwise a banner appears at the top of Atlas. Atlas announces at most one environment every 20 minutes, never the same one twice, never one you already saved or ignored, and only once it stayed open for a moment. **Plus tard** quiets a proposal for 8 hours. Windows notifications follow the Windows settings, including Do Not Disturb; you can also turn them off in Atlas. Atlas removes its notifications when it closes, because it can only answer them while it runs.

In the settings, you can turn the detection off, turn the notifications off, or have Atlas forget everything it observed. Environments you saved are ordinary workspaces: change or delete them like any other.

## Principles

- **Local only.** Atlas works fully offline. There is no account, no server, no synchronization and no update check.
- **Private.** Atlas has no telemetry and collects no data. Its data and logs stay in `%LOCALAPPDATA%\ELPO\Atlas`. The detection is off until you allow it. The repository also turns off the telemetry of its build and test libraries.
- **You stay in control.** Atlas never closes your applications, never deletes your files, never runs scripts, and never saves a detected environment without your choice. See [SECURITY.md](SECURITY.md) for the details.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.404 or later.

```shell
dotnet build Atlas.slnx
dotnet test --solution Atlas.slnx
dotnet run --project src/Atlas.App
```

To run Atlas with another data folder, for example to try it without touching your own data, set the environment variable `ATLAS_DATA_DIR` to a full path.

To build the downloads, install [Inno Setup 6.7](https://jrsoftware.org/isdl.php) and run `./build/publish.ps1` in PowerShell. It writes the installers, the portable archives and `SHA256SUMS.txt` to `artifacts`, for x64 and ARM64. `./build/test-installer.ps1` installs, updates and uninstalls an installer and checks each step; `./build/test-portable.ps1` checks a portable archive.

The [CI workflow](.github/workflows/ci.yml) builds and tests Atlas on x64 and on ARM64, builds the downloads, and runs those two scripts on a computer of each architecture.

The .NET CLI has its own telemetry, which a repository cannot turn off. To disable it on your computer, set the environment variable `DOTNET_CLI_TELEMETRY_OPTOUT` to `1`.

## Documentation

- [docs/architecture.md](docs/architecture.md): how the code is organized.
- [docs/file-format.md](docs/file-format.md): the workspace file format and the import rules.
- [docs/manual-tests.md](docs/manual-tests.md): the checks that need a real Windows session.
- [SECURITY.md](SECURITY.md): reporting a vulnerability, and what Atlas refuses to do.

## License

[MIT](LICENSE). Atlas ships with third-party components under their own licenses: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
