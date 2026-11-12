namespace Atlas.Core.Detection;

/// <summary>
/// What the computer shows at one moment, as the platform reads it: the programs that have a window, the folders open
/// in the file explorer, and the projects those point to. An observation is never stored as such: only the
/// environments built from it are, and only until the user decides about them.
/// </summary>
public sealed record WorkObservation(
    IReadOnlyList<ObservedApplication> Applications,
    IReadOnlyList<string> ExplorerFolders,
    IReadOnlyList<ObservedProject> Projects)
{
    public static readonly WorkObservation Empty = new([], [], []);
}

/// <param name="Name">The name the system gives the program.</param>
/// <param name="Target">What an application resource stores to start it: a program path or an installed application.</param>
/// <param name="OpenItems">Files and folders the program has open, as far as the system can tell.</param>
public sealed record ObservedApplication(string Name, string Target, IReadOnlyList<ObservedItem> OpenItems);

public sealed record ObservedItem(string Path, bool IsFolder);

/// <summary>A folder that holds a project: a Git repository, or a folder with a solution or a package manifest.</summary>
/// <param name="EntryFile">The file that opens the project in its development tool, such as a solution, if there is one.</param>
/// <param name="RepositoryAddress">The web address of the project's remote repository, if it has one.</param>
public sealed record ObservedProject(string Root, string Name, string? EntryFile, string? RepositoryAddress);

/// <summary>Reads the current session. Implementations never open, start, close or change anything.</summary>
public interface IWorkObserver
{
    Task<WorkObservation> ObserveAsync(CancellationToken cancellationToken = default);
}
