# Workspace file format

Atlas exports a workspace to a JSON file, imports such files, and keeps its saved versions in the same format. This page describes version 1 of the format.

## Example

```json
{
  "format": "elpo-atlas-workspace",
  "version": 1,
  "savedAt": "2026-11-08T11:03:13+00:00",
  "workspace": {
    "name": "Reprise du mémoire",
    "description": "Sources et plan",
    "notes": "Plan validé avec Camille",
    "resources": [
      { "kind": "application", "name": "Éditeur", "target": "C:\\Programmes\\Éditeur\\editeur.exe", "requiresReview": false },
      { "kind": "file", "name": "Mémoire", "target": "D:\\Documents\\mémoire.docx", "requiresReview": false },
      { "kind": "folder", "name": "Sources", "target": "\\\\serveur\\partage\\Sources", "requiresReview": false },
      { "kind": "url", "name": "Bibliothèque", "target": "https://example.org/catalogue", "requiresReview": false }
    ]
  }
}
```

## Fields

| Field | Required | Content |
|---|---|---|
| `format` | Yes | Always `elpo-atlas-workspace`. |
| `version` | Yes | The format version, an integer. This page describes `1`. |
| `savedAt` | No | When the file was written, as an ISO 8601 date. Atlas does not use it on import. |
| `workspace.name` | Yes | Up to 120 characters, on one line. |
| `workspace.description` | No | Up to 2,000 characters. |
| `workspace.notes` | No | Up to 1,000,000 characters. |
| `workspace.resources` | No | Up to 1,000 resources. |
| `kind` | Yes | `application`, `file`, `folder` or `url`. Other names are kept as unknown types. |
| `name` | Yes | The name shown in Atlas, up to 120 characters. |
| `target` | Yes | A program (`.exe`) or `shell:AppsFolder\…` entry for an application, a complete path for a file or a folder, an `http` or `https` address for a link. |
| `requiresReview` | No | Used by saved versions. Ignored on import: Atlas decides itself what needs approval. |

## Import rules

Atlas reads the file and nothing else: during an import it does not look up any path, does not contact any server and starts nothing.

- **Strict structure.** A file larger than 2 MB, nested deeper than 16 levels, with comments, trailing commas, a property given twice or a property this page does not list is refused as a whole. There is no field for command-line arguments or a working directory, and adding one makes the file invalid.
- **Newer versions.** A file whose `version` is above 1 comes from a newer Atlas and is refused.
- **Resources that break a rule are left out,** with the reason shown before importing: files that would run code (`.bat`, `.ps1`, `.lnk` and the like), incomplete or ambiguous paths, links other than `http` and `https`, programs that are not `.exe` files. Environment variables such as `%APPDATA%` are not expanded, so a path that uses one is incomplete.
- **Approval.** Imported applications and network locations (`\\server\share`) are marked "À approuver". Atlas does not open them until the user approves each one.
- **Always new.** An import creates a new workspace with new identifiers and today's dates. It never replaces or merges into an existing workspace.

## Privacy

An exported file contains the paths of the resources, which often include the Windows user name, as in `C:\Users\name\Documents`. Atlas warns before exporting when it finds the user name in a path. Notes are exported as they are.
