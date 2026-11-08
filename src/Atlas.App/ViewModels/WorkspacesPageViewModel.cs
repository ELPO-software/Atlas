using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class WorkspacesPageViewModel(PageServices services, INavigator navigator) : PageViewModel
{
    private readonly ILogger _logger = services.Loggers.CreateLogger<WorkspacesPageViewModel>();

    [ObservableProperty]
    public partial string WorkspaceCountText { get; private set; } = Strings.Loading;

    [ObservableProperty]
    public partial IReadOnlyList<WorkspaceCardViewModel> Cards { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial string? LoadError { get; private set; }

    [RelayCommand(AllowConcurrentExecutions = true)]
    public override async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var workspaces = await services.Workspaces.ListAsync();

            WorkspaceCountText = UiText.WorkspaceCount(workspaces.Count);
            IsEmpty = workspaces.Count == 0;
            Cards = workspaces.Select(w => new WorkspaceCardViewModel(w, services.Time, navigator)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspaces could not be read");
            WorkspaceCountText = string.Empty;
            LoadError = UiText.Format(Strings.WorkspacesReadFailed, services.Paths.LogsDirectory);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task CreateAsync()
    {
        var created = await services.Dialogs.EditWorkspaceAsync(new WorkspaceEditorRequest(
            Strings.NewWorkspaceDialogTitle,
            Strings.Create,
            Name: string.Empty,
            Description: string.Empty,
            ShowDescription: true,
            (name, description) => services.Workspaces.CreateAsync(name, description)));

        if (created is not null)
            await navigator.OpenWorkspaceAsync(created.Id);
    }
}
