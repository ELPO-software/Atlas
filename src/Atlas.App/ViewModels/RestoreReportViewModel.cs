using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Core.Restoration;

namespace Atlas.App.ViewModels;

/// <param name="IsProblem">True when the resource is missing or failed, so that it stands out.</param>
public sealed record RestoreLineViewModel(string Name, string Status, string? Detail, bool IsProblem)
{
    public bool HasDetail => Detail is not null;
}

public sealed record RestoreReportViewModel(string Title, string Summary, IReadOnlyList<RestoreLineViewModel> Lines)
{
    public static RestoreReportViewModel From(RestoreReport report) =>
        new(
            report.WasCancelled ? Strings.RestoreStoppedTitle : Strings.RestoreFinishedTitle,
            RestoreText.Summary(report),
            report.Steps
                .Select(step => new RestoreLineViewModel(
                    step.Resource.DisplayName,
                    RestoreText.Status(step),
                    RestoreText.Detail(step),
                    step.Status is RestoreStatus.Missing or RestoreStatus.Failed))
                .ToList());
}
