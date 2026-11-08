using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;

namespace Atlas.Core.Restoration;

/// <param name="SkipReason">Why Atlas will not open this resource, or null if it will.</param>
public sealed record PlannedStep(Resource Resource, RestoreReason? SkipReason);

/// <summary>
/// What a restoration will do, decided before anything is opened: applications first, so that the files they
/// open later find them running, then files and folders, then links.
/// </summary>
public sealed class RestorePlan
{
    private RestorePlan(Guid workspaceId, IReadOnlyList<PlannedStep> steps)
    {
        WorkspaceId = workspaceId;
        Steps = steps;
    }

    public Guid WorkspaceId { get; }

    public IReadOnlyList<PlannedStep> Steps { get; }

    /// <summary>The programs that will start. The user confirms before several of them start at once.</summary>
    public IReadOnlyList<Resource> ApplicationsToLaunch =>
        Steps.Where(s => s.SkipReason is null && s.Resource.Kind == ResourceKind.Application).Select(s => s.Resource).ToList();

    public static RestorePlan For(Workspace workspace) =>
        new(
            workspace.Id,
            workspace.Resources
                .OrderBy(r => PhaseOf(r.Kind))
                .ThenBy(r => r.SortOrder)
                .Select(r => new PlannedStep(r, SkipReasonFor(r)))
                .ToList());

    private static int PhaseOf(ResourceKind kind)
    {
        if (kind == ResourceKind.Application)
            return 0;
        if (kind == ResourceKind.File || kind == ResourceKind.Folder)
            return 1;

        return kind == ResourceKind.Url ? 2 : 3;
    }

    private static RestoreReason? SkipReasonFor(Resource resource)
    {
        var known = resource.Kind == ResourceKind.Application || resource.Kind == ResourceKind.File
            || resource.Kind == ResourceKind.Folder || resource.Kind == ResourceKind.Url;
        if (!known)
            return RestoreReason.UnknownKind;

        // Checked before anything touches the target: a network path from an imported file is not even looked up.
        if (resource.RequiresReview)
            return RestoreReason.NeedsApproval;

        // The rules are checked again here because stored data may predate them or come from elsewhere.
        return ResourceRules.CheckTarget(resource.Kind, resource.Target) switch
        {
            null => null,
            ValidationCode.ExecutableFile => RestoreReason.BlockedFileType,
            _ => RestoreReason.InvalidTarget,
        };
    }
}
