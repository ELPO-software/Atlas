using Atlas.Core.History;
using Atlas.Core.Resources;

namespace Atlas.Core.Workspaces;

/// <summary>
/// Stores workspaces and their resources. Each write is atomic, and stores its line of history in the same
/// transaction: a change and its record apply together or not at all.
/// </summary>
public interface IWorkspaceRepository
{
    /// <summary>Returns every workspace, most recently modified first.</summary>
    Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<Workspace?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>The type and target of every resource of every workspace. A type Atlas cannot name is left out.</summary>
    Task<IReadOnlyList<(ResourceKind Kind, string Target)>> ListResourceTargetsAsync(CancellationToken cancellationToken = default);

    /// <param name="record">The history line of this change, or null to record nothing.</param>
    Task AddAsync(Workspace workspace, HistoryEntry? record = null, CancellationToken cancellationToken = default);

    /// <summary>Saves the workspace and replaces its resources.</summary>
    /// <returns>False if the workspace no longer exists, in which case nothing was written.</returns>
    Task<bool> UpdateAsync(Workspace workspace, HistoryEntry? record = null, CancellationToken cancellationToken = default);

    /// <summary>Deletes the workspace with its resources and versions. Its history is kept.</summary>
    /// <returns>False if the workspace did not exist.</returns>
    Task<bool> DeleteAsync(Guid id, HistoryEntry? record = null, CancellationToken cancellationToken = default);
}
