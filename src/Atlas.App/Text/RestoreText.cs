using Atlas.App.Resources;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;

namespace Atlas.App.Text;

/// <summary>Says what happened to each resource, in words that do not claim more than the system confirmed.</summary>
internal static class RestoreText
{
    public static string Status(RestoreStepResult step) => step.Status switch
    {
        RestoreStatus.Restored => step.Reason == RestoreReason.Launched ? Strings.StatusLaunched : Strings.StatusOpened,
        RestoreStatus.Missing => Strings.StatusMissing,
        RestoreStatus.Failed => Strings.StatusFailed,
        _ => Strings.StatusSkipped,
    };

    public static string? Detail(RestoreStepResult step) => step.Reason switch
    {
        RestoreReason.Launched or RestoreReason.Opened => null,
        RestoreReason.NotFound => NotFound(step.Resource.Kind),
        RestoreReason.WrongType => step.Resource.Kind == ResourceKind.Folder
            ? Strings.ReasonWrongTypeFileFound
            : Strings.ReasonWrongTypeFolderFound,
        RestoreReason.ExitedImmediately => UiText.Format(Strings.ReasonExitedImmediately, step.SystemMessage),
        RestoreReason.NoAssociatedApplication => Strings.ReasonNoAssociatedApplication,
        RestoreReason.SystemRefused => string.IsNullOrWhiteSpace(step.SystemMessage)
            ? Strings.ReasonSystemRefused
            : UiText.Format(Strings.ReasonSystemRefusedWithMessage, step.SystemMessage),
        RestoreReason.TimedOut => Strings.ReasonTimedOut,
        RestoreReason.NeedsApproval => Strings.ReasonNeedsApproval,
        RestoreReason.BlockedFileType => Strings.ReasonBlockedFileType,
        RestoreReason.InvalidTarget => Strings.ReasonInvalidTarget,
        RestoreReason.UnknownKind => Strings.ReasonUnknownKind,
        _ => Strings.ReasonCancelled,
    };

    public static string Summary(RestoreReport report)
    {
        var parts = new List<string>();
        Add(parts, report.Count(RestoreStatus.Restored), Strings.CountRestoredOne, Strings.CountRestoredMany);
        Add(parts, report.Count(RestoreStatus.Missing), Strings.CountMissingOne, Strings.CountMissingMany);
        Add(parts, report.Count(RestoreStatus.Failed), Strings.CountFailedOne, Strings.CountFailedMany);
        Add(parts, report.Count(RestoreStatus.Skipped), Strings.CountSkippedOne, Strings.CountSkippedMany);
        return string.Join(", ", parts);
    }

    /// <summary>Shows where a resource points, in words for an installed application whose identifier means nothing to people.</summary>
    public static string Target(Resource resource) =>
        resource.Kind == ResourceKind.Application && ResourceTargets.TryGetInstalledApplicationId(resource.Target, out _)
            ? Strings.InstalledApplicationTarget
            : resource.Target;

    private static string NotFound(ResourceKind kind)
    {
        if (kind == ResourceKind.Application)
            return Strings.ReasonNotFoundApplication;

        return kind == ResourceKind.Folder ? Strings.ReasonNotFoundFolder : Strings.ReasonNotFoundFile;
    }

    private static void Add(List<string> parts, int count, string one, string many)
    {
        if (count > 0)
            parts.Add(UiText.Format(count == 1 ? one : many, count));
    }
}
