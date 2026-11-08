using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Core.Workspaces;
using CommunityToolkit.Mvvm.Input;

namespace Atlas.App.ViewModels;

/// <summary>A workspace as it appears in a list.</summary>
public sealed class WorkspaceCardViewModel(WorkspaceSummary summary, TimeProvider time, INavigator navigator)
{
    public Guid Id => summary.Id;

    public string Name => summary.Name;

    public string Description => summary.Description;

    public string ResourceCountText { get; } = UiText.ResourceCount(summary.ResourceCount);

    public string ModifiedText { get; } = DateText.Modified(summary.UpdatedAt, time);

    public string OpenLabel { get; } = UiText.Format(Strings.OpenWorkspaceLabel, summary.Name);

    public IAsyncRelayCommand OpenCommand { get; } =
        new AsyncRelayCommand(() => navigator.OpenWorkspaceAsync(summary.Id), AsyncRelayCommandOptions.AllowConcurrentExecutions);
}
