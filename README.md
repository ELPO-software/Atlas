# Atlas

A local-first workspace manager to organize and restore your working environments.

Atlas is part of the ELPO software suite. It saves the applications, files, folders, links and notes of an activity, so you can bring them all back in a few clicks.

## Status

Atlas is an alpha: version 0.1.0-alpha.2. Its interface is in French. It is built for 64-bit Windows 10 and 11, and has been tested on Windows 11. Expect rough edges, and keep your own copies of what matters.

## What it does

- **Workspaces** gather the applications, files, folders and web links of an activity, with notes.
- **Restore** opens them all: applications first, then files and folders, then links. Atlas asks before starting several programs, never closes anything, and reports what opened, what was missing and what failed.
- **Versions** keep the state of a workspace, so you can go back to it. Going back first saves the current state.
- **History** lists what was done, including deleted workspaces.
- **Import and export** a workspace as a JSON file. An import shows what it contains before creating anything, and imported applications wait for your approval.
- **Themes:** light, dark, or following Windows.

## Install

Each [release](https://github.com/ELPO-software/Atlas/releases) offers two downloads. Neither needs anything else installed: both include the .NET runtime.

- **`Atlas-0.1.0-alpha.2-win-x64.exe`**, a single file to run. On its first start, it extracts the native libraries it contains to a folder named after it in `%TEMP%\.net`, such as `%TEMP%\.net\Atlas-0.1.0-alpha.2-win-x64`.
- **`Atlas-0.1.0-alpha.2-win-x64.zip`**, a folder to extract anywhere, then run `Atlas.exe` from it. It extracts nothing at run time.

Optionally, check the SHA-256 checksum of the download against the one published with the release, with `Get-FileHash` in PowerShell.

Atlas is not code-signed yet. On the first start, Windows SmartScreen may say it protected your PC: choose **More info**, then **Run anyway**.

To uninstall, delete the executable or the extracted folder. For the executable, also delete its folder in `%TEMP%\.net`, named after it; leave the other folders there, which belong to other applications. To remove your data as well, delete `%LOCALAPPDATA%\ELPO\Atlas`.

## Principles

- **Local only.** Atlas works fully offline. There is no account, no server and no synchronization.
- **Private.** Atlas has no telemetry and collects no data. Its data and logs stay in `%LOCALAPPDATA%\ELPO\Atlas`. The repository also turns off the telemetry of its build and test libraries.
- **You stay in control.** Atlas never closes your applications, never deletes your files and never runs scripts. See [SECURITY.md](SECURITY.md) for the details.

## Building from source

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.404 or later.

```shell
dotnet build Atlas.slnx
dotnet test --solution Atlas.slnx
dotnet run --project src/Atlas.App
```

To build the release downloads, run `./build/publish.ps1` in PowerShell. It writes the executable, the archive and their checksums to `artifacts`.

The .NET CLI has its own telemetry, which a repository cannot turn off. To disable it on your computer, set the environment variable `DOTNET_CLI_TELEMETRY_OPTOUT` to `1`.

## Documentation

- [docs/architecture.md](docs/architecture.md): how the code is organized.
- [docs/file-format.md](docs/file-format.md): the workspace file format and the import rules.
- [docs/manual-tests.md](docs/manual-tests.md): the checks that need a real Windows session.
- [SECURITY.md](SECURITY.md): reporting a vulnerability, and what Atlas refuses to do.

## License

[MIT](LICENSE). Atlas ships with third-party components under their own licenses: see [THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).
