using System.Security.Cryptography;
using System.Text;
using Atlas.Core.Resources;

namespace Atlas.Core.Detection;

/// <summary>
/// Turns an observation into environments to propose. It decides from the observation alone: it never looks at the
/// disk or at the system, so every decision can be tested.
/// </summary>
public static class EnvironmentDetector
{
    public const int MaxFilesPerProject = 5;
    public const int MaxContextApplications = 6;
    public const int MaxContextFolders = 4;
    public const int MaxContextFiles = 6;

    /// <summary>Fewer resources than this make a context too thin to be worth proposing.</summary>
    public const int MinContextResources = 3;

    private const string ProjectKeyPrefix = "project:";
    private const string ContextKeyPrefix = "context:";

    public static IReadOnlyList<EnvironmentProposal> Propose(WorkObservation observation)
    {
        ArgumentNullException.ThrowIfNull(observation);

        var proposals = new List<EnvironmentProposal>();
        var projects = observation.Projects.DistinctBy(p => PathText.Key(p.Root)).ToList();

        foreach (var project in projects)
        {
            if (ProposeProject(project, observation) is { } proposal)
                proposals.Add(proposal);
        }

        if (ProposeContext(observation, projects.Select(p => p.Root).ToList()) is { } context)
            proposals.Add(context);

        return proposals;
    }

    public static bool IsProjectKey(string key) => key.StartsWith(ProjectKeyPrefix, StringComparison.Ordinal);

    /// <summary>The share of resources two proposals have in common, from 0 (none) to 1 (the same).</summary>
    public static double Similarity(IReadOnlyList<ProposedResource> first, IReadOnlyList<ProposedResource> second)
    {
        var a = first.Select(Identity).ToHashSet(StringComparer.Ordinal);
        var b = second.Select(Identity).ToHashSet(StringComparer.Ordinal);
        if (a.Count == 0 && b.Count == 0)
            return 1;

        var shared = a.Count(b.Contains);
        return (double)shared / (a.Count + b.Count - shared);
    }

    /// <summary>What makes two resources the same, whatever their names: their type and their target.</summary>
    public static string Identity(ProposedResource resource) =>
        resource.Kind.Value + "|" + (resource.Kind == ResourceKind.Url ? resource.Target : PathText.Key(resource.Target));

    private static EnvironmentProposal? ProposeProject(ObservedProject project, WorkObservation observation)
    {
        var root = project.Root;
        var applications = observation.Applications
            .Where(a => a.OpenItems.Any(item => PathText.IsInside(item.Path, root)))
            .ToList();
        var openInExplorer = observation.ExplorerFolders.Any(folder => PathText.IsInside(folder, root));

        if (applications.Count == 0 && !openInExplorer)
            return null;

        var resources = new ProposalResources();
        resources.Add(ResourceKind.Folder, project.Name, root, ProposalReason.ProjectFolder);

        if (project.EntryFile is { } entryFile)
            resources.Add(ResourceKind.File, Path.GetFileName(entryFile), entryFile, ProposalReason.ProjectEntryFile);

        foreach (var application in applications)
            resources.Add(ResourceKind.Application, application.Name, application.Target, ProposalReason.OpenInApplication);

        var files = applications
            .SelectMany(a => a.OpenItems)
            .Where(item => !item.IsFolder && PathText.IsInside(item.Path, root))
            .DistinctBy(item => PathText.Key(item.Path))
            .Take(MaxFilesPerProject);
        foreach (var file in files)
            resources.Add(ResourceKind.File, Path.GetFileName(file.Path), file.Path, ProposalReason.OpenInApplication);

        if (project.RepositoryAddress is { } address && ResourceTargets.TryGetWebAddress(address, out var web))
            resources.Add(ResourceKind.Url, web.Host + web.AbsolutePath.TrimEnd('/'), address, ProposalReason.Repository);

        if (resources.Count < 2)
            return null;

        // A program that has the project open is direct evidence; a folder window alone is a clue.
        var confidence = applications.Count > 0 ? DetectionConfidence.Detected : DetectionConfidence.Probable;
        return new EnvironmentProposal(ProjectKeyPrefix + PathText.Key(root), ProposalKind.Project, project.Name, confidence, resources.ToList());
    }

    private static EnvironmentProposal? ProposeContext(WorkObservation observation, IReadOnlyList<string> projectRoots)
    {
        bool InProject(string path) => projectRoots.Any(root => PathText.IsInside(path, root));

        var applications = observation.Applications
            .Where(a => !a.OpenItems.Any(item => InProject(item.Path)))
            .ToList();

        var resources = new ProposalResources();
        var names = new List<string>();
        foreach (var application in applications)
        {
            if (names.Count == MaxContextApplications)
                break;

            if (resources.Add(ResourceKind.Application, application.Name, application.Target, ProposalReason.RunningApplication))
                names.Add(application.Name);
        }

        if (names.Count == 0)
            return null;

        var folders = 0;
        foreach (var folder in observation.ExplorerFolders.Where(f => !InProject(f)))
        {
            if (folders < MaxContextFolders && resources.Add(ResourceKind.Folder, FolderName(folder), folder, ProposalReason.OpenInExplorer))
                folders++;
        }

        var items = 0;
        foreach (var item in applications.SelectMany(a => a.OpenItems).Where(i => !InProject(i.Path)))
        {
            if (items == MaxContextFiles)
                break;

            var added = item.IsFolder
                ? resources.Add(ResourceKind.Folder, FolderName(item.Path), item.Path, ProposalReason.OpenInApplication)
                : resources.Add(ResourceKind.File, Path.GetFileName(item.Path), item.Path, ProposalReason.OpenInApplication);
            if (added)
                items++;
        }

        if (resources.Count < MinContextResources)
            return null;

        var list = resources.ToList();
        return new EnvironmentProposal(ContextKey(list), ProposalKind.RecurringContext, string.Join(", ", names.Take(3)), DetectionConfidence.Probable, list);
    }

    private static string ContextKey(IReadOnlyList<ProposedResource> resources)
    {
        var identities = string.Join("\n", resources.Select(Identity).Order(StringComparer.Ordinal));
        return ContextKeyPrefix + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identities)), 0, 12);
    }

    private static string FolderName(string path)
    {
        var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(path));
        return string.IsNullOrEmpty(name) ? path : name;
    }

    /// <summary>Collects valid resources once each. Anything the rules of Atlas refuse is left out.</summary>
    private sealed class ProposalResources
    {
        private readonly List<ProposedResource> _resources = [];
        private readonly HashSet<string> _identities = new(StringComparer.Ordinal);

        public int Count => _resources.Count;

        public bool Add(ResourceKind kind, string name, string target, ProposalReason reason)
        {
            var displayName = name.Trim();
            if (displayName.Length > ResourceRules.DisplayNameMaxLength)
                displayName = displayName[..ResourceRules.DisplayNameMaxLength].TrimEnd();
            if (displayName.Length == 0)
                displayName = target.Trim();

            if (ResourceRules.Validate(kind, displayName, target).Count > 0)
                return false;

            var resource = new ProposedResource(kind, displayName, target.Trim(), reason);
            if (!_identities.Add(Identity(resource)))
                return false;

            _resources.Add(resource);
            return true;
        }

        public List<ProposedResource> ToList() => [.. _resources];
    }
}

/// <summary>Compares Windows paths the way Windows does: without regard to case or to a final separator.</summary>
public static class PathText
{
    public static string Key(string path) => Path.TrimEndingDirectorySeparator(path.Trim()).ToUpperInvariant();

    /// <summary>True if <paramref name="path"/> is <paramref name="root"/> or lies anywhere below it.</summary>
    public static bool IsInside(string path, string root)
    {
        var candidate = Path.TrimEndingDirectorySeparator(path.Trim());
        var folder = Path.TrimEndingDirectorySeparator(root.Trim());

        if (candidate.Equals(folder, StringComparison.OrdinalIgnoreCase))
            return true;

        return candidate.Length > folder.Length
            && candidate.StartsWith(folder, StringComparison.OrdinalIgnoreCase)
            && (candidate[folder.Length] is '\\' or '/' || folder.EndsWith('\\') || folder.EndsWith('/'));
    }
}
