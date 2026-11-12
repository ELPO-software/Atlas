using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using Atlas.Core.Detection;
using Atlas.Core.Resources;

namespace Atlas.Platform.Windows.Detection;

/// <summary>
/// Reads which files and folders a program has open, from what the system tells about it. Every function here works on
/// text only; checking that a path exists is left to the caller, through <c>exists</c>.
/// </summary>
public static class OpenItems
{
    /// <summary>
    /// The files and folders named on a command line. Only complete paths count, and never the program itself, nor
    /// locations that belong to the system or to installed programs. The command line itself is never kept.
    /// </summary>
    /// <param name="arguments">The arguments, the program first, as Windows splits them.</param>
    /// <param name="exists">Tells whether a path exists, and whether it is a folder; null if it does not exist.</param>
    /// <param name="excludedRoots">Folders whose content is never a document of the user, such as Program Files.</param>
    public static IReadOnlyList<ObservedItem> FromCommandLine(
        IReadOnlyList<string> arguments, Func<string, bool?> exists, IReadOnlyList<string> excludedRoots)
    {
        ArgumentNullException.ThrowIfNull(arguments);
        ArgumentNullException.ThrowIfNull(exists);

        var items = new List<ObservedItem>();
        foreach (var raw in arguments.Skip(1))
        {
            var argument = raw.Trim().Trim('"');

            // Switches such as --folder-uri=file:///c%3A/... carry their value after the sign.
            var equals = argument.IndexOf('=', StringComparison.Ordinal);
            if (argument.StartsWith('-') && equals > 0)
                argument = argument[(equals + 1)..].Trim('"');

            if (FileUriToPath(argument) is { } fromUri)
                argument = fromUri;

            if (!ResourceTargets.IsUsablePath(argument) || ResourceTargets.IsNetworkPath(argument))
                continue;

            if (excludedRoots.Any(root => PathText.IsInside(argument, root)))
                continue;

            if (exists(argument) is not { } isFolder)
                continue;

            if (!isFolder && ResourceTargets.RunsCode(argument))
                continue;

            if (items.All(i => !string.Equals(PathText.Key(i.Path), PathText.Key(argument), StringComparison.Ordinal)))
                items.Add(new ObservedItem(argument, isFolder));
        }

        return items;
    }

    /// <summary>
    /// The folders a Visual Studio Code family editor knows, from its storage.json file: the windows it reopens and
    /// the folders it recently opened. Which one is open is decided by the window titles.
    /// </summary>
    public static IReadOnlyList<string> FoldersFromCodeStorage(string json)
    {
        var folders = new List<string>();
        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return folders;

            if (root.TryGetProperty("windowsState", out var windows) && windows.ValueKind == JsonValueKind.Object)
            {
                if (windows.TryGetProperty("lastActiveWindow", out var last))
                    AddFolder(last, folders);

                if (windows.TryGetProperty("openedWindows", out var opened) && opened.ValueKind == JsonValueKind.Array)
                {
                    foreach (var window in opened.EnumerateArray())
                        AddFolder(window, folders);
                }
            }

            if (root.TryGetProperty("profileAssociations", out var profiles) && profiles.ValueKind == JsonValueKind.Object
                && profiles.TryGetProperty("workspaces", out var workspaces) && workspaces.ValueKind == JsonValueKind.Object)
            {
                foreach (var workspace in workspaces.EnumerateObject())
                    AddUri(workspace.Name, folders);
            }

            if (root.TryGetProperty("backupWorkspaces", out var backups) && backups.ValueKind == JsonValueKind.Object
                && backups.TryGetProperty("folders", out var backupFolders) && backupFolders.ValueKind == JsonValueKind.Array)
            {
                foreach (var folder in backupFolders.EnumerateArray())
                {
                    if (folder.ValueKind == JsonValueKind.Object && folder.TryGetProperty("folderUri", out var uri) && uri.ValueKind == JsonValueKind.String)
                        AddUri(uri.GetString(), folders);
                }
            }
        }
        catch (JsonException)
        {
            // An editor that changed its format just gives no clue.
        }

        return folders;
    }

    /// <summary>The solutions Visual Studio recently opened, from its ApplicationPrivateSettings.xml file.</summary>
    public static IReadOnlyList<string> SolutionsFromVisualStudioSettings(string xml)
    {
        var solutions = new List<string>();
        string? json;
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(xml), settings);
            json = XDocument.Load(reader)
                .Descendants("collection")
                .FirstOrDefault(c => (string?)c.Attribute("name") == "CodeContainers.Offline")?
                .Elements("value")
                .FirstOrDefault()?
                .Value;
        }
        catch (XmlException)
        {
            return solutions;
        }

        if (string.IsNullOrWhiteSpace(json))
            return solutions;

        try
        {
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 32 });
            if (document.RootElement.ValueKind != JsonValueKind.Array)
                return solutions;

            foreach (var entry in document.RootElement.EnumerateArray())
            {
                if (entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("Key", out var key) && key.ValueKind == JsonValueKind.String
                    && key.GetString() is { } path && ResourceTargets.IsUsablePath(path))
                {
                    solutions.Add(path);
                }
            }
        }
        catch (JsonException)
        {
            // Same as above: an unknown format gives no clue.
        }

        return solutions;
    }

    /// <summary>
    /// The candidates whose name appears as a whole part of a window title, such as "Atlas" in
    /// "Program.cs - Atlas - Visual Studio Code". A name shared by two candidates is ambiguous and matches nothing.
    /// </summary>
    /// <param name="nameOf">The name to look for in the titles, such as a folder name or a solution name.</param>
    public static IReadOnlyList<string> MatchTitles(IReadOnlyList<string> titles, IReadOnlyList<string> candidates, Func<string, string> nameOf)
    {
        ArgumentNullException.ThrowIfNull(titles);
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(nameOf);

        var parts = titles
            .SelectMany(t => t.Split([" - ", " — ", " – "], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Select(p => p.TrimStart('●', '*', ' ').Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return candidates
            .DistinctBy(PathText.Key)
            .GroupBy(nameOf, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() == 1 && g.Key.Length > 0 && parts.Contains(g.Key))
            .Select(g => g.First())
            .ToList();
    }

    private static void AddFolder(JsonElement window, List<string> folders)
    {
        if (window.ValueKind == JsonValueKind.Object && window.TryGetProperty("folder", out var folder) && folder.ValueKind == JsonValueKind.String)
            AddUri(folder.GetString(), folders);
    }

    private static void AddUri(string? value, List<string> folders)
    {
        if (FileUriToPath(value) is { } path && folders.All(f => PathText.Key(f) != PathText.Key(path)))
            folders.Add(path);
    }

    /// <summary>
    /// Reads a local file address such as file:///c%3A/Users/L%C3%A9a, as editors write them, into a Windows path.
    /// Addresses of remote machines or other systems give null.
    /// </summary>
    internal static string? FileUriToPath(string? value)
    {
        if (value is null || !value.StartsWith("file:///", StringComparison.OrdinalIgnoreCase))
            return null;

        string path;
        try
        {
            path = Uri.UnescapeDataString(value["file:///".Length..]).Replace('/', '\\');
        }
        catch (UriFormatException)
        {
            return null;
        }

        if (path.Length >= 2 && path[1] == ':')
            path = char.ToUpperInvariant(path[0]) + path[1..];

        return ResourceTargets.IsUsablePath(path) ? path : null;
    }
}
