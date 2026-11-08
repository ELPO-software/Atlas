using Atlas.Core.Resources;
using Atlas.Core.Workspaces;

namespace Atlas.App.Services;

/// <summary>One line of what a confirmation is about, such as a program and its location.</summary>
public sealed record ConfirmationDetail(string Title, string Description);

/// <summary>A decision the user must take explicitly. Cancelling is the default answer.</summary>
public sealed record ConfirmationRequest(string Title, string Message, string ConfirmLabel, string CancelLabel)
{
    public IReadOnlyList<ConfirmationDetail> Details { get; init; } = [];

    /// <summary>False for a decision that destroys nothing, such as starting programs.</summary>
    public bool IsDestructive { get; init; } = true;

    public bool HasDetails => Details.Count > 0;
}

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

/// <param name="FixedKind">The type of the resource being edited, or null when the user chooses it for a new resource.</param>
/// <param name="Submit">Saves the values, with the same guarantee as for a workspace.</param>
public sealed record ResourceEditorRequest(
    string Title,
    string ConfirmLabel,
    ResourceKind? FixedKind,
    string DisplayName,
    string Target,
    Func<ResourceKind, string?, string?, Task<Workspace>> Submit);

public interface IDialogService
{
    /// <returns>True only if the user explicitly confirmed.</returns>
    Task<bool> ConfirmAsync(ConfirmationRequest request);

    /// <returns>The saved workspace, or null if the user cancelled.</returns>
    Task<Workspace?> EditWorkspaceAsync(WorkspaceEditorRequest request);

    /// <returns>The workspace with the saved resource, or null if the user cancelled.</returns>
    Task<Workspace?> EditResourceAsync(ResourceEditorRequest request);
}
