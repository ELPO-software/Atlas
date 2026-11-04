namespace Atlas.Core.Workspaces;

/// <summary>Stores workspaces and their resources. Each write is atomic: it applies completely or not at all.</summary>
public interface IWorkspaceRepository
{
    /// <summary>Returns every workspace, most recently modified first.</summary>
    Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default);

    Task<int> CountAsync(CancellationToken cancellationToken = default);

    Task<Workspace?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default);

    /// <summary>Saves the workspace and replaces its resources.</summary>
    /// <returns>False if the workspace no longer exists, in which case nothing was written.</returns>
    Task<bool> UpdateAsync(Workspace workspace, CancellationToken cancellationToken = default);

    /// <returns>False if the workspace did not exist.</returns>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}
