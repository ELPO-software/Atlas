using Atlas.Core.Resources;

namespace Atlas.Core.Workspaces;

/// <summary>
/// The operations the application performs on workspaces. Asking the user to confirm a deletion is the
/// interface's job; once this class is called, the decision has been made.
/// </summary>
public sealed class WorkspaceService(IWorkspaceRepository repository, TimeProvider timeProvider)
{
    // A change reads a workspace and writes it back whole. Running changes one at a time keeps the automatic
    // save of the notes from overwriting a rename that lands between its read and its write.
    private readonly SemaphoreSlim _changeGate = new(1, 1);

    public Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        repository.ListAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        repository.CountAsync(cancellationToken);

    public async Task<Workspace> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await repository.GetAsync(id, cancellationToken) ?? throw new WorkspaceNotFoundException(id);

    public async Task<Workspace> CreateAsync(
        string? name, string? description, CancellationToken cancellationToken = default)
    {
        var workspace = Workspace.Create(name, description, timeProvider.GetUtcNow());
        await repository.AddAsync(workspace, cancellationToken);
        return workspace;
    }

    public Task<Workspace> UpdateDetailsAsync(
        Guid id, string? name, string? description, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (workspace, now) => workspace.UpdateDetails(name, description, now), cancellationToken);

    public Task<Workspace> UpdateNotesAsync(
        Guid id, string? notes, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (workspace, now) => workspace.UpdateNotes(notes, now), cancellationToken);

    public Task<Workspace> AddResourceAsync(
        Guid id, ResourceKind kind, string? displayName, string? target, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (workspace, now) => workspace.AddResource(kind, displayName, target, now), cancellationToken);

    public Task<Workspace> UpdateResourceAsync(
        Guid id, Guid resourceId, string? displayName, string? target, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (workspace, now) => workspace.UpdateResource(resourceId, displayName, target, now), cancellationToken);

    public Task<Workspace> RemoveResourceAsync(Guid id, Guid resourceId, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (workspace, now) => workspace.RemoveResource(resourceId, now), cancellationToken);

    /// <summary>Records that the workspace was restored. Its modification date does not move.</summary>
    public Task<Workspace> MarkUsedAsync(Guid id, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, (workspace, now) => workspace.MarkUsed(now), cancellationToken);

    public async Task<Workspace> DuplicateAsync(
        Guid id, string? newName, CancellationToken cancellationToken = default)
    {
        await _changeGate.WaitAsync(cancellationToken);
        try
        {
            var source = await GetAsync(id, cancellationToken);
            var copy = source.Duplicate(newName, timeProvider.GetUtcNow());
            await repository.AddAsync(copy, cancellationToken);
            return copy;
        }
        finally
        {
            _changeGate.Release();
        }
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        await _changeGate.WaitAsync(cancellationToken);
        try
        {
            if (!await repository.DeleteAsync(id, cancellationToken))
                throw new WorkspaceNotFoundException(id);
        }
        finally
        {
            _changeGate.Release();
        }
    }

    private async Task<Workspace> ChangeAsync(
        Guid id, Action<Workspace, DateTimeOffset> change, CancellationToken cancellationToken)
    {
        await _changeGate.WaitAsync(cancellationToken);
        try
        {
            var workspace = await GetAsync(id, cancellationToken);
            change(workspace, timeProvider.GetUtcNow());

            if (!await repository.UpdateAsync(workspace, cancellationToken))
                throw new WorkspaceNotFoundException(id);

            return workspace;
        }
        finally
        {
            _changeGate.Release();
        }
    }
}
