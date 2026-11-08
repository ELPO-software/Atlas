using Atlas.Core.Resources;

namespace Atlas.Core.Restoration;

public enum RestoreStatus
{
    /// <summary>The system accepted to open or launch the resource. Atlas cannot see what happens after that.</summary>
    Restored,

    /// <summary>The file, folder or application is not there.</summary>
    Missing,

    Failed,

    /// <summary>Atlas chose not to open the resource, and says why.</summary>
    Skipped,
}

public enum RestoreReason
{
    Launched,
    Opened,
    NotFound,

    /// <summary>A folder where a file was expected, or the reverse.</summary>
    WrongType,

    /// <summary>The program stopped with an error right after it started.</summary>
    ExitedImmediately,

    NoAssociatedApplication,
    SystemRefused,

    /// <summary>The system did not answer in time. The resource may still open later.</summary>
    TimedOut,

    NeedsApproval,
    BlockedFileType,
    InvalidTarget,
    UnknownKind,

    /// <summary>The user stopped the restoration before this resource.</summary>
    Cancelled,
}

/// <summary>What the system answered when asked to open a resource.</summary>
/// <param name="SystemMessage">The system's own explanation of a failure, when it gave one.</param>
/// <param name="Confirmation">
/// For a program that could still stop right after starting: the final result, known once the program has had time to fail.
/// </param>
public sealed record OpenResult(
    RestoreStatus Status,
    RestoreReason Reason,
    string? SystemMessage = null,
    Task<OpenResult>? Confirmation = null)
{
    public static OpenResult Launched(Task<OpenResult>? confirmation = null) =>
        new(RestoreStatus.Restored, RestoreReason.Launched, Confirmation: confirmation);

    public static OpenResult Opened() => new(RestoreStatus.Restored, RestoreReason.Opened);

    public static OpenResult NotFound() => new(RestoreStatus.Missing, RestoreReason.NotFound);

    public static OpenResult Failed(RestoreReason reason, string? systemMessage = null) =>
        new(RestoreStatus.Failed, reason, systemMessage);

    public static OpenResult Skipped(RestoreReason reason) => new(RestoreStatus.Skipped, reason);
}

public sealed record RestoreStepResult(Resource Resource, RestoreStatus Status, RestoreReason Reason, string? SystemMessage);

public sealed record RestoreReport(
    Guid WorkspaceId,
    DateTimeOffset StartedAt,
    DateTimeOffset FinishedAt,
    IReadOnlyList<RestoreStepResult> Steps)
{
    public int Count(RestoreStatus status) => Steps.Count(step => step.Status == status);

    public bool WasCancelled => Steps.Any(step => step.Reason == RestoreReason.Cancelled);
}

/// <param name="Completed">How many resources are done.</param>
/// <param name="Current">The resource being opened, or null once all are done.</param>
public sealed record RestoreProgress(int Completed, int Total, Resource? Current);

/// <summary>Opens resources through the operating system.</summary>
public interface IResourceOpener
{
    /// <summary>Checks that the resource is there, then asks the system to open it. Expected failures are returned, not thrown.</summary>
    Task<OpenResult> OpenAsync(ResourceKind kind, string target);
}
