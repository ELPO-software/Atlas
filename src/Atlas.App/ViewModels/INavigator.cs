namespace Atlas.App.ViewModels;

/// <summary>Moves between pages. Each call lets the current page finish its work first, and it may refuse to leave.</summary>
public interface INavigator
{
    Task OpenHomeAsync();

    Task OpenWorkspacesAsync();

    Task OpenWorkspaceAsync(Guid workspaceId);

    Task OpenSettingsAsync();

    /// <param name="focusKey">The suggestion to show first, if any.</param>
    Task OpenSuggestionsAsync(string? focusKey = null);
}
