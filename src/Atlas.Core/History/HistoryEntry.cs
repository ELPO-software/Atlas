namespace Atlas.Core.History;

public enum HistoryAction
{
    WorkspaceCreated,
    WorkspaceDuplicated,
    WorkspaceImported,
    WorkspaceEdited,
    NotesEdited,
    ResourceAdded,
    ResourceEdited,
    ResourceRemoved,
    ResourceApproved,
    WorkspaceDeleted,
    VersionSaved,
    VersionRestored,
    WorkspaceRestored,
    WorkspaceExported,
}

public sealed record RestoreCounts(int Restored, int Missing, int Failed, int Skipped)
{
    public bool HasProblems => Missing > 0 || Failed > 0;
}

/// <summary>One line of the history. Lines are written with the change they describe, in the same transaction.</summary>
/// <param name="WorkspaceName">The name at the time, so the line stays readable after a rename or a deletion.</param>
/// <param name="Detail">What the action was about, such as a resource, a version or a file name.</param>
/// <param name="Counts">For a restoration, what happened to the resources.</param>
public sealed record HistoryEntry(
    DateTimeOffset OccurredAt,
    HistoryAction Action,
    Guid WorkspaceId,
    string WorkspaceName,
    string Detail = "",
    RestoreCounts? Counts = null);

/// <summary>
/// Keeps the history. It is never deleted with a workspace: the history of a deleted workspace stays readable.
/// </summary>
public interface IHistoryRepository
{
    /// <summary>Returns the most recent lines first.</summary>
    Task<IReadOnlyList<HistoryEntry>> ListAsync(int limit, CancellationToken cancellationToken = default);

    /// <summary>Records an action that changes nothing else, such as an export.</summary>
    Task RecordAsync(HistoryEntry entry, CancellationToken cancellationToken = default);
}
