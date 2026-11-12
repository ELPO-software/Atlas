using System.Diagnostics;
using System.Runtime.Versioning;
using Atlas.Core.Detection;
using Atlas.Core.Resources;

namespace Atlas.Platform.Windows.Detection;

/// <summary>
/// Observes the session on Windows: the programs that have a window, what they have open, the folders open in File
/// Explorer, and the projects behind them. It reads; it never opens, starts, closes or changes anything, and nothing it
/// reads leaves the computer.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsWorkObserver(IApplicationCatalog catalog, ProjectLocator projects) : IWorkObserver
{
    public const int MaxApplications = 20;
    public const int MaxProjects = 12;

    private const long MaxSettingsFileSize = 8 * 1024 * 1024;
    private static readonly TimeSpan CatalogLifetime = TimeSpan.FromMinutes(10);

    /// <summary>Parts of Windows and helpers that have windows but are not tools anyone works with.</summary>
    private static readonly HashSet<string> IgnoredPrograms = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "ApplicationFrameHost.exe", "ShellExperienceHost.exe", "StartMenuExperienceHost.exe",
        "SearchHost.exe", "SearchApp.exe", "TextInputHost.exe", "LockApp.exe", "SystemSettings.exe", "Taskmgr.exe",
        "sihost.exe", "ctfmon.exe", "dwm.exe", "rundll32.exe", "dllhost.exe", "OpenWith.exe", "PickerHost.exe",
        "CredentialUIBroker.exe", "consent.exe", "WerFault.exe", "Widgets.exe", "WidgetBoard.exe", "GameBar.exe",
        "GameBarFTServer.exe", "msedgewebview2.exe", "smartscreen.exe", "SecurityHealthSystray.exe", "conhost.exe",
        "PhoneExperienceHost.exe", "CrossDeviceResume.exe", "ScreenClippingHost.exe", "UserOOBEBroker.exe",
        "NVIDIA Overlay.exe", "ShellHost.exe", "Atlas.exe",
    };

    /// <summary>Store applications that are part of Windows itself, by the start of their identifier.</summary>
    private static readonly string[] IgnoredApplicationIds =
    [
        "windows.immersivecontrolpanel", "Microsoft.Windows.ShellExperienceHost", "MicrosoftWindows.Client.",
        "Microsoft.Windows.StartMenuExperienceHost", "Microsoft.Windows.Search", "Microsoft.LockApp",
        "Microsoft.XboxGamingOverlay", "Microsoft.Windows.CapturePicker", "MicrosoftWindows.LKG.",
    ];

    /// <summary>Editors of the Visual Studio Code family, and the name of their folder in %APPDATA%.</summary>
    private static readonly Dictionary<string, string> CodeEditors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Code.exe"] = "Code",
        ["Code - Insiders.exe"] = "Code - Insiders",
        ["Cursor.exe"] = "Cursor",
        ["VSCodium.exe"] = "VSCodium",
        ["Windsurf.exe"] = "Windsurf",
    };

    private readonly SemaphoreSlim _catalogGate = new(1, 1);
    private Dictionary<string, string>? _catalogNames;
    private DateTime _catalogReadAt;

    public Task<WorkObservation> ObserveAsync(CancellationToken cancellationToken = default) =>
        Task.Run(() => ObserveCoreAsync(cancellationToken), cancellationToken);

    private async Task<WorkObservation> ObserveCoreAsync(CancellationToken cancellationToken)
    {
        var names = await CatalogNamesAsync(cancellationToken);
        var excludedRoots = SystemFolders();
        var applications = new List<ObservedApplication>();

        foreach (var program in ProgramsWithWindows())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (applications.Count == MaxApplications)
                break;

            if (Describe(program, names, excludedRoots) is { } application)
                applications.Add(application);
        }

        var explorerFolders = ExplorerWindows.ListFolders();

        var found = new List<ObservedProject>();
        var paths = applications.SelectMany(a => a.OpenItems.Select(i => i.Path)).Concat(explorerFolders);
        foreach (var path in paths)
        {
            if (found.Count == MaxProjects)
                break;

            if (found.Any(p => PathText.IsInside(path, p.Root)))
                continue;

            if (projects.Find(path) is { } project && found.All(p => PathText.Key(p.Root) != PathText.Key(project.Root)))
                found.Add(project);
        }

        return new WorkObservation(applications, explorerFolders, found);
    }

    /// <summary>The programs with a window, each with the titles of its windows and the processes that show them.</summary>
    private static List<Program> ProgramsWithWindows()
    {
        var self = Environment.ProcessId;
        var programs = new Dictionary<string, Program>(StringComparer.OrdinalIgnoreCase);

        foreach (var window in WindowsSession.ListApplicationWindows())
        {
            if (window.ProcessId == self)
                continue;

            var image = WindowsSession.ImagePath(window.ProcessId);
            if (image is null)
                continue;

            var processId = window.ProcessId;

            // A Store application draws inside a frame that belongs to ApplicationFrameHost.exe.
            if (Path.GetFileName(image).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
            {
                if (WindowsSession.HostedProcess(window.Handle, window.ProcessId) is not { } hosted)
                    continue;

                processId = hosted;
                image = WindowsSession.ImagePath(hosted) ?? image;
            }

            var applicationId = WindowsSession.PackagedApplicationId(processId);
            var target = applicationId is null ? image : ResourceTargets.ForInstalledApplication(applicationId);

            if (!programs.TryGetValue(target, out var program))
            {
                program = new Program(target, image, applicationId, [], []);
                programs.Add(target, program);
            }

            program.Titles.Add(window.Title);
            if (!program.ProcessIds.Contains(processId))
                program.ProcessIds.Add(processId);
        }

        return [.. programs.Values];
    }

    private static ObservedApplication? Describe(Program program, Dictionary<string, string> names, IReadOnlyList<string> excludedRoots)
    {
        var fileName = Path.GetFileName(program.Image);
        if (program.ApplicationId is null && IgnoredPrograms.Contains(fileName))
            return null;

        if (program.ApplicationId is { } id && IgnoredApplicationIds.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            return null;

        var name = names.GetValueOrDefault(program.Target) ?? (program.ApplicationId is null ? ProgramName(program.Image) : null);
        if (string.IsNullOrWhiteSpace(name) || ResourceRules.CheckTarget(ResourceKind.Application, program.Target) is not null)
            return null;

        var items = new List<ObservedItem>();
        if (program.ApplicationId is null)
        {
            var ownFolder = Path.GetDirectoryName(program.Image);
            var roots = ownFolder is null ? excludedRoots : [.. excludedRoots, ownFolder];
            foreach (var processId in program.ProcessIds)
            {
                if (WindowsSession.CommandLine(processId) is { } arguments)
                    items.AddRange(OpenItems.FromCommandLine(arguments, Exists, roots));
            }
        }

        if (CodeEditors.TryGetValue(fileName, out var editorFolder))
            items.AddRange(CodeEditorFolders(editorFolder, program.Titles));
        else if (fileName.Equals("devenv.exe", StringComparison.OrdinalIgnoreCase))
            items.AddRange(VisualStudioSolutions(program.Titles));

        var distinct = items.DistinctBy(i => PathText.Key(i.Path)).Take(20).ToList();
        return new ObservedApplication(name.Trim(), program.Target, distinct);
    }

    private static IEnumerable<ObservedItem> CodeEditorFolders(string editorFolder, IReadOnlyList<string> titles)
    {
        var storage = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), editorFolder, "User", "globalStorage", "storage.json");
        if (ReadSmallFile(storage) is not { } json)
            return [];

        var folders = OpenItems.FoldersFromCodeStorage(json).Where(Directory.Exists).ToList();
        return OpenItems.MatchTitles(titles, folders, f => Path.GetFileName(Path.TrimEndingDirectorySeparator(f)))
            .Select(f => new ObservedItem(f, true));
    }

    private static IEnumerable<ObservedItem> VisualStudioSolutions(IReadOnlyList<string> titles)
    {
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "VisualStudio");
        if (!Directory.Exists(root))
            return [];

        var solutions = new List<string>();
        try
        {
            foreach (var instance in Directory.EnumerateDirectories(root))
            {
                if (ReadSmallFile(Path.Combine(instance, "ApplicationPrivateSettings.xml")) is { } xml)
                    solutions.AddRange(OpenItems.SolutionsFromVisualStudioSettings(xml).Where(File.Exists));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }

        return OpenItems.MatchTitles(titles, solutions, Path.GetFileNameWithoutExtension)
            .Select(s => new ObservedItem(s, false));
    }

    private static string? ReadSmallFile(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists && file.Length <= MaxSettingsFileSize ? File.ReadAllText(path) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string ProgramName(string image)
    {
        try
        {
            var version = FileVersionInfo.GetVersionInfo(image);
            var name = new[] { version.FileDescription, version.ProductName }.FirstOrDefault(n => !string.IsNullOrWhiteSpace(n));
            if (name is not null)
                return name.Trim();
        }
        catch (FileNotFoundException)
        {
            // The program was removed while it runs: its file name will do.
        }

        return Path.GetFileNameWithoutExtension(image);
    }

    private static bool? Exists(string path)
    {
        try
        {
            if (File.Exists(path))
                return false;

            return Directory.Exists(path) ? true : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    /// <summary>Folders whose content belongs to the system or to installed programs, never to the user's work.</summary>
    private static List<string> SystemFolders() =>
        new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Windows),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            Path.GetTempPath(),
        }.Where(f => !string.IsNullOrEmpty(f)).ToList();

    private async Task<Dictionary<string, string>> CatalogNamesAsync(CancellationToken cancellationToken)
    {
        await _catalogGate.WaitAsync(cancellationToken);
        try
        {
            if (_catalogNames is null || DateTime.UtcNow - _catalogReadAt > CatalogLifetime)
            {
                var installed = await catalog.ListAsync(cancellationToken);
                _catalogNames = installed
                    .GroupBy(a => a.Target, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.First().Name, StringComparer.OrdinalIgnoreCase);
                _catalogReadAt = DateTime.UtcNow;
            }

            return _catalogNames;
        }
        finally
        {
            _catalogGate.Release();
        }
    }

    private sealed record Program(string Target, string Image, string? ApplicationId, List<string> Titles, List<int> ProcessIds);
}
