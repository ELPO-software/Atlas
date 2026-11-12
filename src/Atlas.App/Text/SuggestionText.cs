using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.Core.Detection;
using Atlas.Core.Notifications;
using Atlas.Core.Resources;

namespace Atlas.App.Text;

/// <summary>The words Atlas uses for detected environments. A probable one is never worded as a certainty.</summary>
internal static class SuggestionText
{
    private const int NamedApplications = 2;

    public static string Kind(ProposalKind kind) => kind == ProposalKind.Project ? Strings.SuggestionKindProject : Strings.SuggestionKindContext;

    public static string Confidence(DetectionConfidence confidence) =>
        confidence == DetectionConfidence.Detected ? Strings.SuggestionDetected : Strings.SuggestionProbable;

    /// <summary>Why Atlas proposes this environment, in one sentence.</summary>
    public static string Explanation(StoredProposal proposal)
    {
        if (proposal.Proposal.Kind == ProposalKind.RecurringContext)
            return UiText.Format(Strings.SuggestionWhyContext, proposal.SeenDays);

        var applications = proposal.Proposal.Resources
            .Where(r => r.Kind == ResourceKind.Application)
            .Select(r => r.DisplayName)
            .ToList();

        return applications.Count > 0
            ? UiText.Format(Strings.SuggestionWhyOpenIn, List(applications))
            : Strings.SuggestionWhyExplorer;
    }

    /// <summary>What the environment holds, such as "le dossier, Visual Studio Code et 3 fichiers".</summary>
    public static string Summary(IReadOnlyList<ProposedResource> resources)
    {
        var parts = new List<string>();

        var folders = resources.Count(r => r.Kind == ResourceKind.Folder);
        if (folders > 0)
            parts.Add(folders == 1 ? Strings.SummaryOneFolder : UiText.Format(Strings.SummaryFolders, folders));

        var applications = resources.Where(r => r.Kind == ResourceKind.Application).Select(r => r.DisplayName).ToList();
        parts.AddRange(applications.Take(NamedApplications));
        if (applications.Count > NamedApplications)
            parts.Add(UiText.Format(Strings.SummaryOtherApplications, applications.Count - NamedApplications));

        var files = resources.Count(r => r.Kind == ResourceKind.File);
        if (files > 0)
            parts.Add(files == 1 ? Strings.SummaryOneFile : UiText.Format(Strings.SummaryFiles, files));

        var links = resources.Count(r => r.Kind == ResourceKind.Url);
        if (links > 0)
            parts.Add(links == 1 ? Strings.SummaryOneLink : UiText.Format(Strings.SummaryLinks, links));

        return List(parts);
    }

    public static string Reason(ProposalReason reason) => reason switch
    {
        ProposalReason.ProjectFolder => Strings.ReasonProjectFolder,
        ProposalReason.ProjectEntryFile => Strings.ReasonEntryFile,
        ProposalReason.Repository => Strings.ReasonRepository,
        ProposalReason.OpenInApplication => Strings.ReasonOpenInApplication,
        ProposalReason.OpenInExplorer => Strings.ReasonOpenInExplorer,
        _ => Strings.ReasonRunningApplication,
    };

    public static DesktopNotification Notification(StoredProposal proposal, string tag)
    {
        var environment = proposal.Proposal;
        var summary = Summary(environment.Resources);

        if (environment.Confidence == DetectionConfidence.Detected)
        {
            return new DesktopNotification(
                tag,
                Strings.NotificationDetectedTitle,
                UiText.Format(Strings.NotificationDetectedMessage, environment.Title, summary),
                [
                    new(Strings.SuggestionSave, SuggestionCenter.SaveArgument),
                    new(Strings.SuggestionLater, SuggestionCenter.LaterArgument),
                    new(Strings.SuggestionIgnore, SuggestionCenter.IgnoreArgument),
                ]);
        }

        return new DesktopNotification(
            tag,
            Strings.NotificationProbableTitle,
            UiText.Format(Strings.NotificationProbableMessage, Title(proposal), summary),
            [
                new(Strings.SuggestionReview, SuggestionCenter.ReviewArgument),
                new(Strings.SuggestionLater, SuggestionCenter.LaterArgument),
                new(Strings.SuggestionIgnore, SuggestionCenter.IgnoreArgument),
            ]);
    }

    /// <summary>The name shown for an environment, and proposed for its workspace.</summary>
    public static string Title(StoredProposal proposal) =>
        proposal.Proposal.Kind == ProposalKind.Project
            ? proposal.Proposal.Title
            : UiText.Format(Strings.SuggestionContextTitle, proposal.Proposal.Title);

    /// <summary>Joins words the French way: "a, b et c".</summary>
    public static string List(IReadOnlyList<string> items) => items.Count switch
    {
        0 => string.Empty,
        1 => items[0],
        _ => UiText.Format(Strings.ListAnd, string.Join(", ", items.Take(items.Count - 1)), items[^1]),
    };
}
