using Atlas.Core.Exchange;
using Atlas.Core.History;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;

namespace Atlas.Core.Versions;

/// <summary>
/// Saves versions of a workspace and goes back to them. Going back first saves the current state as an automatic
/// version, so that nothing is lost and the user can return to where they were.
/// </summary>
public sealed class VersionService(WorkspaceService workspaces, IVersionRepository versions, TimeProvider timeProvider)
{
    public Task<IReadOnlyList<VersionSummary>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        versions.ListAsync(workspaceId, cancellationToken);

    public Task<int> CountAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        versions.CountAsync(workspaceId, cancellationToken);

    public async Task<VersionSummary> SaveAsync(Guid workspaceId, string? name, CancellationToken cancellationToken = default)
    {
        var errors = VersionRules.ValidateName(name);
        if (errors.Count > 0)
            throw new AtlasValidationException(errors);

        var version = await SaveCurrentStateAsync(workspaceId, name!.Trim(), isAutomatic: false, cancellationToken);
        return Summary(version);
    }

    /// <param name="safetyVersionName">The name of the automatic version that keeps the current state.</param>
    /// <returns>The workspace as it is after going back.</returns>
    public async Task<Workspace> RestoreAsync(Guid versionId, string safetyVersionName, CancellationToken cancellationToken = default)
    {
        var version = await versions.GetAsync(versionId, cancellationToken) ?? throw new VersionNotFoundException(versionId);
        var read = WorkspaceDocument.Read(version.Content);
        if (read.Content is null)
            throw new UnreadableVersionException(versionId);

        // Saved first, in its own transaction: if going back fails, the current state is still safe.
        await SaveCurrentStateAsync(version.WorkspaceId, safetyVersionName, isAutomatic: true, cancellationToken);
        return await workspaces.RevertAsync(version.WorkspaceId, read.Content, version.Name, cancellationToken);
    }

    private async Task<WorkspaceVersion> SaveCurrentStateAsync(
        Guid workspaceId, string name, bool isAutomatic, CancellationToken cancellationToken)
    {
        var workspace = await workspaces.GetAsync(workspaceId, cancellationToken);
        var now = timeProvider.GetUtcNow();
        var version = new WorkspaceVersion(
            Guid.NewGuid(), workspaceId, name, now, isAutomatic, workspace.Resources.Count, WorkspaceDocument.Write(workspace, now));

        await versions.AddAsync(
            version, new HistoryEntry(now, HistoryAction.VersionSaved, workspace.Id, workspace.Name, name), cancellationToken);
        return version;
    }

    private static VersionSummary Summary(WorkspaceVersion version) =>
        new(version.Id, version.WorkspaceId, version.Name, version.SavedAt, version.IsAutomatic, version.ResourceCount);
}
