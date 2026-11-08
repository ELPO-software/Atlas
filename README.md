# Atlas

A local-first workspace manager to organize and restore your working environments.

Atlas is part of the ELPO software suite. It saves the applications, files, folders, links and notes of an activity, so you can bring them all back in a few clicks.

## Status

Atlas is in early development. The first Alpha is being built in stages. You can already gather applications, files, folders and links in workspaces, keep notes in them and restore them on Windows. Versions, history and import and export come next.

## Principles

- **Local only.** Atlas works fully offline. There is no account, no server and no synchronization.
- **Private.** The application has no telemetry and collects no data. The repository also turns off the telemetry of its build and test libraries.
- **You stay in control.** Atlas never closes your applications, never deletes your files and never runs scripts.

## Building from source

Atlas targets Windows first, and its architecture leaves room for Linux and macOS. You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0), version 10.0.403 or later.

```shell
dotnet build Atlas.slnx
dotnet test --solution Atlas.slnx
dotnet run --project src/Atlas.App
```

Atlas keeps its data and logs in `%LOCALAPPDATA%\ELPO\Atlas`.

The .NET CLI has its own telemetry, which a repository cannot turn off. To disable it on your computer, set the environment variable `DOTNET_CLI_TELEMETRY_OPTOUT` to `1`.

See [docs/architecture.md](docs/architecture.md) for how the code is organized, and [docs/manual-tests.md](docs/manual-tests.md) for the checks that need a real Windows session.

## License

[MIT](LICENSE)
