using Atlas.App.Resources;
using Atlas.Core.History;

namespace Atlas.App.Text;

internal static class HistoryText
{
    public static string Describe(HistoryEntry entry) => entry.Action switch
    {
        HistoryAction.WorkspaceCreated => UiText.Format(Strings.HistoryWorkspaceCreated, entry.WorkspaceName),
        HistoryAction.WorkspaceDuplicated => UiText.Format(Strings.HistoryWorkspaceDuplicated, entry.WorkspaceName, entry.Detail),
        HistoryAction.WorkspaceImported => UiText.Format(Strings.HistoryWorkspaceImported, entry.WorkspaceName, entry.Detail),
        HistoryAction.WorkspaceEdited => UiText.Format(Strings.HistoryWorkspaceEdited, entry.WorkspaceName),
        HistoryAction.NotesEdited => UiText.Format(Strings.HistoryNotesEdited, entry.WorkspaceName),
        HistoryAction.ResourceAdded => UiText.Format(Strings.HistoryResourceAdded, entry.WorkspaceName, entry.Detail),
        HistoryAction.ResourceEdited => UiText.Format(Strings.HistoryResourceEdited, entry.WorkspaceName, entry.Detail),
        HistoryAction.ResourceRemoved => UiText.Format(Strings.HistoryResourceRemoved, entry.WorkspaceName, entry.Detail),
        HistoryAction.ResourceApproved => UiText.Format(Strings.HistoryResourceApproved, entry.WorkspaceName, entry.Detail),
        HistoryAction.WorkspaceDeleted => UiText.Format(Strings.HistoryWorkspaceDeleted, entry.WorkspaceName),
        HistoryAction.VersionSaved => UiText.Format(Strings.HistoryVersionSaved, entry.WorkspaceName, entry.Detail),
        HistoryAction.VersionRestored => UiText.Format(Strings.HistoryVersionRestored, entry.WorkspaceName, entry.Detail),
        HistoryAction.WorkspaceRestored => UiText.Format(Strings.HistoryWorkspaceRestored, entry.WorkspaceName, Counts(entry.Counts)),
        _ => UiText.Format(Strings.HistoryWorkspaceExported, entry.WorkspaceName, entry.Detail),
    };

    /// <summary>A restoration where something was missing or failed stands out in the list.</summary>
    public static bool IsProblem(HistoryEntry entry) => entry.Counts?.HasProblems == true;

    private static string Counts(RestoreCounts? counts) =>
        counts is null ? string.Empty : RestoreText.Summary(counts.Restored, counts.Missing, counts.Failed, counts.Skipped);
}
