using Atlas.Core.Detection;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;

namespace Atlas.Platform;

/// <summary>Stands in on systems Atlas cannot open resources on yet. It says so instead of pretending.</summary>
public sealed class UnsupportedResourceOpener : IResourceOpener
{
    public Task<OpenResult> OpenAsync(ResourceKind kind, string target) =>
        Task.FromResult(OpenResult.Failed(RestoreReason.SystemRefused, "Atlas cannot open resources on this system yet."));
}

public sealed class EmptyApplicationCatalog : IApplicationCatalog
{
    public Task<IReadOnlyList<InstalledApplication>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<InstalledApplication>>([]);
}

/// <summary>Observes nothing, on systems where Atlas cannot read the session yet.</summary>
public sealed class NoWorkObserver : IWorkObserver
{
    public Task<WorkObservation> ObserveAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(WorkObservation.Empty);
}
