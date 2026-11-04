using Atlas.Core.Workspaces;

namespace Atlas.Core.Tests;

/// <summary>
/// Hands out copies, as a database would. A service that changed a workspace without saving it
/// would therefore fail the tests instead of passing by accident.
/// </summary>
internal sealed class InMemoryWorkspaceRepository : IWorkspaceRepository
{
    private readonly Dictionary<Guid, Workspace> _workspaces = [];

    public int WriteCount { get; private set; }

    /// <summary>Simulates another operation deleting the workspace just before the next update.</summary>
    public bool LoseWorkspaceBeforeUpdate { get; set; }

    public Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<WorkspaceSummary>>(
            _workspaces.Values
                .OrderByDescending(w => w.UpdatedAt)
                .Select(w => new WorkspaceSummary(w.Id, w.Name, w.Description, w.Resources.Count, w.UpdatedAt, w.LastUsedAt))
                .ToList());

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(_workspaces.Count);

    public Task<Workspace?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        Task.FromResult(_workspaces.TryGetValue(id, out var workspace) ? Copy(workspace) : null);

    public Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        WriteCount++;
        _workspaces.Add(workspace.Id, Copy(workspace));
        return Task.CompletedTask;
    }

    public Task<bool> UpdateAsync(Workspace workspace, CancellationToken cancellationToken = default)
    {
        if (LoseWorkspaceBeforeUpdate)
            _workspaces.Remove(workspace.Id);

        if (!_workspaces.ContainsKey(workspace.Id))
            return Task.FromResult(false);

        WriteCount++;
        _workspaces[workspace.Id] = Copy(workspace);
        return Task.FromResult(true);
    }

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        WriteCount++;
        return Task.FromResult(_workspaces.Remove(id));
    }

    private static Workspace Copy(Workspace w) =>
        Workspace.Restore(w.Id, w.Name, w.Description, w.Notes, w.CreatedAt, w.UpdatedAt, w.LastUsedAt, w.Resources);
}
