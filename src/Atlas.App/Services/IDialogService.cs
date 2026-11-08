using Atlas.Core.Workspaces;

namespace Atlas.App.Services;

/// <summary>A destructive decision. Cancelling is the default answer.</summary>
public sealed record ConfirmationRequest(string Title, string Message, string ConfirmLabel, string CancelLabel);

/// <param name="ShowDescription">False when the form only asks for a name.</param>
/// <param name="Submit">
/// Saves the values. The form stays open until this succeeds, so a refused value or a failed save loses nothing the user typed.
/// </param>
public sealed record WorkspaceEditorRequest(
    string Title,
    string ConfirmLabel,
    string Name,
    string Description,
    bool ShowDescription,
    Func<string?, string?, Task<Workspace>> Submit);

public interface IDialogService
{
    /// <returns>True only if the user explicitly confirmed.</returns>
    Task<bool> ConfirmAsync(ConfirmationRequest request);

    /// <returns>The saved workspace, or null if the user cancelled.</returns>
    Task<Workspace?> EditWorkspaceAsync(WorkspaceEditorRequest request);
}
