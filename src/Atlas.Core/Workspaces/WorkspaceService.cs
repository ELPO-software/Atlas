using Atlas.Core.Exchange;
using Atlas.Core.History;
using Atlas.Core.Resources;

namespace Atlas.Core.Workspaces;

/// <summary>
/// The operations the application performs on workspaces. Each change is stored together with its line of history.
/// Asking the user to confirm a deletion is the interface's job; once this class is called, the decision has been made.
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
        var now = timeProvider.GetUtcNow();
        var workspace = Workspace.Create(name, description, now);
        await repository.AddAsync(workspace, Record(now, HistoryAction.WorkspaceCreated, workspace), cancellationToken);
        return workspace;
    }

    /// <summary>
    /// Creates a workspace from an environment Atlas detected and the user chose to save, with the resources they kept.
    /// Every resource is checked as if the user had added it by hand.
    /// </summary>
    public async Task<Workspace> CreateFromDetectionAsync(
        string? name,
        IReadOnlyList<(ResourceKind Kind, string DisplayName, string Target)> resources,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resources);

        var now = timeProvider.GetUtcNow();
        var workspace = Workspace.Create(name, string.Empty, now);
        foreach (var (kind, displayName, target) in resources)
            workspace.AddResource(kind, displayName, target, now);

        await repository.AddAsync(workspace, Record(now, HistoryAction.WorkspaceDetected, workspace), cancellationToken);
        return workspace;
    }

    /// <summary>The type and target of every resource of every workspace, to recognize what the user already keeps.</summary>
    public Task<IReadOnlyList<(ResourceKind Kind, string Target)>> ListResourceTargetsAsync(CancellationToken cancellationToken = default) =>
        repository.ListResourceTargetsAsync(cancellationToken);

    /// <param name="source">Where the workspace comes from, such as the file name, for the history.</param>
    public async Task<Workspace> ImportAsync(ImportPlan plan, string source, CancellationToken cancellationToken = default)
    {
        var now = timeProvider.GetUtcNow();
        var workspace = Workspace.Import(plan, now);
        await repository.AddAsync(workspace, Record(now, HistoryAction.WorkspaceImported, workspace, source), cancellationToken);
        return workspace;
    }

    public Task<Workspace> UpdateDetailsAsync(
        Guid id, string? name, string? description, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, HistoryAction.WorkspaceEdited, (workspace, now) =>
        {
            workspace.UpdateDetails(name, description, now);
            return string.Empty;
        }, cancellationToken);

    public Task<Workspace> UpdateNotesAsync(
        Guid id, string? notes, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, HistoryAction.NotesEdited, (workspace, now) =>
        {
            workspace.UpdateNotes(notes, now);
            return string.Empty;
        }, cancellationToken);

    public Task<Workspace> AddResourceAsync(
        Guid id, ResourceKind kind, string? displayName, string? target, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, HistoryAction.ResourceAdded, (workspace, now) =>
            workspace.AddResource(kind, displayName, target, now).DisplayName, cancellationToken);

    public Task<Workspace> UpdateResourceAsync(
        Guid id, Guid resourceId, string? displayName, string? target, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, HistoryAction.ResourceEdited, (workspace, now) =>
            workspace.UpdateResource(resourceId, displayName, target, now).DisplayName, cancellationToken);

    public Task<Workspace> RemoveResourceAsync(Guid id, Guid resourceId, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, HistoryAction.ResourceRemoved, (workspace, now) =>
        {
            var name = workspace.Resources.FirstOrDefault(r => r.Id == resourceId)?.DisplayName ?? string.Empty;
            workspace.RemoveResource(resourceId, now);
            return name;
        }, cancellationToken);

    /// <summary>The user allows Atlas to open a resource that came from an imported file.</summary>
    public Task<Workspace> ApproveResourceAsync(Guid id, Guid resourceId, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, HistoryAction.ResourceApproved, (workspace, now) =>
            workspace.ApproveResource(resourceId, now).DisplayName, cancellationToken);

    /// <summary>Records a restoration and what happened to the resources. The modification date does not move.</summary>
    public Task<Workspace> MarkUsedAsync(Guid id, RestoreCounts counts, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            id,
            HistoryAction.WorkspaceRestored,
            (workspace, now) =>
            {
                workspace.MarkUsed(now);
                return string.Empty;
            },
            cancellationToken,
            counts);

    /// <summary>Puts back the content of a saved version. The caller saves the current state first.</summary>
    public Task<Workspace> RevertAsync(Guid id, DocumentContent version, string versionName, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, HistoryAction.VersionRestored, (workspace, now) =>
        {
            workspace.RevertTo(version, now);
            return versionName;
        }, cancellationToken);

    public async Task<Workspace> DuplicateAsync(
        Guid id, string? newName, CancellationToken cancellationToken = default)
    {
        await _changeGate.WaitAsync(cancellationToken);
        try
        {
            var source = await GetAsync(id, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var copy = source.Duplicate(newName, now);
            await repository.AddAsync(copy, Record(now, HistoryAction.WorkspaceDuplicated, copy, source.Name), cancellationToken);
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
            var workspace = await GetAsync(id, cancellationToken);
            var record = Record(timeProvider.GetUtcNow(), HistoryAction.WorkspaceDeleted, workspace);

            if (!await repository.DeleteAsync(id, record, cancellationToken))
                throw new WorkspaceNotFoundException(id);
        }
        finally
        {
            _changeGate.Release();
        }
    }

    /// <param name="change">Applies the change and returns the detail of its history line.</param>
    private async Task<Workspace> ChangeAsync(
        Guid id,
        HistoryAction action,
        Func<Workspace, DateTimeOffset, string> change,
        CancellationToken cancellationToken,
        RestoreCounts? counts = null)
    {
        await _changeGate.WaitAsync(cancellationToken);
        try
        {
            var workspace = await GetAsync(id, cancellationToken);
            var now = timeProvider.GetUtcNow();
            var detail = change(workspace, now);

            if (!await repository.UpdateAsync(workspace, Record(now, action, workspace, detail, counts), cancellationToken))
                throw new WorkspaceNotFoundException(id);

            return workspace;
        }
        finally
        {
            _changeGate.Release();
        }
    }

    private static HistoryEntry Record(
        DateTimeOffset now, HistoryAction action, Workspace workspace, string detail = "", RestoreCounts? counts = null) =>
        new(now, action, workspace.Id, workspace.Name, detail, counts);
}
