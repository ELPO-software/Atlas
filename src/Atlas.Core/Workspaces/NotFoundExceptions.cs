namespace Atlas.Core.Workspaces;

public sealed class WorkspaceNotFoundException(Guid workspaceId)
    : Exception($"Workspace {workspaceId} does not exist.")
{
    public Guid WorkspaceId { get; } = workspaceId;
}

public sealed class ResourceNotFoundException(Guid workspaceId, Guid resourceId)
    : Exception($"Resource {resourceId} does not exist in workspace {workspaceId}.")
{
    public Guid WorkspaceId { get; } = workspaceId;

    public Guid ResourceId { get; } = resourceId;
}
