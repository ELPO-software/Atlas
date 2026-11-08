# Security

## Reporting a vulnerability

Please do not describe a vulnerability in a public issue. Open an issue titled "Security contact", with no details, and a maintainer will reply with a private way to send them. You will get an answer, and credit if you wish, once the fix is published.

Atlas is an alpha. Only the latest release receives fixes.

## What Atlas does and does not do

Atlas opens files, folders, links and programs on request, so the rules below are part of its design. A way around any of them is a vulnerability.

- **It works offline.** Atlas has no network code and sends nothing. A test fails if Atlas code references a networking library. Opening a link hands it to the default browser.
- **It never runs code from a file.** Files that would run code when opened, such as scripts, shortcuts and installers, are refused as files, including names Windows reads the same way, like `script.bat.`. Windows' own list of dangerous types is checked as well. Programs are added as applications, and they start without any command-line argument.
- **It refuses ambiguous locations:** incomplete paths, device paths, wildcards, alternate data streams such as `notes.txt:hidden.exe`, and links other than `http` and `https`. Environment variables are never expanded.
- **It asks before starting several programs,** and it never closes anything.
- **Imports are inert.** An imported file is read, never followed: during an import no path is looked up and no server is contacted. The reader refuses files over 2 MB, deep nesting, repeated or unknown properties and more than 1,000 resources. Imported applications and network locations are not opened until the user approves each one.
- **It never deletes the user's files.** Deleting a workspace deletes what Atlas stored about it, and the confirmation says so. The only files Atlas removes on its own are its old log files.

## Where data lives

Atlas keeps its database and logs in `%LOCALAPPDATA%\ELPO\Atlas`, readable by the Windows account that runs it. The database is not encrypted: anyone who can read that folder can read the workspaces and notes. Exported files contain the locations of the resources, which often reveal the Windows user name; Atlas warns before exporting them.

## Distribution

The release archive is not code-signed yet, so Windows SmartScreen warns before the first start. Each release lists the SHA-256 checksum of its archive; compare it before running Atlas.
