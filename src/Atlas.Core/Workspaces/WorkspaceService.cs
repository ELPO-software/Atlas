namespace Atlas.Core.Workspaces;

/// <summary>
/// The operations the application performs on workspaces. Asking the user to confirm a deletion is the
/// interface's job; once this class is called, the decision has been made.
/// </summary>
public sealed class WorkspaceService(IWorkspaceRepository repository, TimeProvider timeProvider)
{
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

    public async Task<Workspace> UpdateDetailsAsync(
        Guid id, string? name, string? description, CancellationToken cancellationToken = default)
    {
        var workspace = await GetAsync(id, cancellationToken);
        workspace.UpdateDetails(name, description, timeProvider.GetUtcNow());
        await SaveAsync(workspace, cancellationToken);
        return workspace;
    }

    public async Task<Workspace> UpdateNotesAsync(
        Guid id, string? notes, CancellationToken cancellationToken = default)
    {
        var workspace = await GetAsync(id, cancellationToken);
        workspace.UpdateNotes(notes, timeProvider.GetUtcNow());
        await SaveAsync(workspace, cancellationToken);
        return workspace;
    }

    public async Task<Workspace> DuplicateAsync(
        Guid id, string? newName, CancellationToken cancellationToken = default)
    {
        var source = await GetAsync(id, cancellationToken);
        var copy = source.Duplicate(newName, timeProvider.GetUtcNow());
        await repository.AddAsync(copy, cancellationToken);
        return copy;
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        if (!await repository.DeleteAsync(id, cancellationToken))
            throw new WorkspaceNotFoundException(id);
    }

    private async Task SaveAsync(Workspace workspace, CancellationToken cancellationToken)
    {
        if (!await repository.UpdateAsync(workspace, cancellationToken))
            throw new WorkspaceNotFoundException(workspace.Id);
    }
}
