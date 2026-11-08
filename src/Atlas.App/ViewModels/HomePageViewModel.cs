using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class HomePageViewModel(PageServices services, INavigator navigator) : PageViewModel
{
    private const int RecentCount = 5;

    private readonly ILogger _logger = services.Loggers.CreateLogger<HomePageViewModel>();
    private bool _isCreating;

    [ObservableProperty]
    public partial string WorkspaceCountText { get; private set; } = Strings.Loading;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRecents))]
    public partial IReadOnlyList<WorkspaceCardViewModel> Recents { get; private set; } = [];

    [ObservableProperty]
    public partial bool IsEmpty { get; private set; }

    [ObservableProperty]
    public partial string? LoadError { get; private set; }

    [ObservableProperty]
    public partial string NewWorkspaceName { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NewWorkspaceError { get; private set; }

    public bool HasRecents => Recents.Count > 0;

    [RelayCommand(AllowConcurrentExecutions = true)]
    public override async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var workspaces = await services.Workspaces.ListAsync();

            WorkspaceCountText = UiText.WorkspaceCount(workspaces.Count);
            IsEmpty = workspaces.Count == 0;
            Recents = workspaces
                .OrderByDescending(w => w.LastActivityAt)
                .Take(RecentCount)
                .Select(w => new WorkspaceCardViewModel(w, services.Time, navigator))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspaces could not be read");
            WorkspaceCountText = string.Empty;
            LoadError = UiText.Format(Strings.WorkspacesReadFailed, services.Paths.LogsDirectory);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task QuickCreateAsync()
    {
        // Pressing Enter twice must not create two workspaces.
        if (_isCreating)
            return;

        _isCreating = true;
        NewWorkspaceError = null;
        try
        {
            var workspace = await services.Workspaces.CreateAsync(NewWorkspaceName, description: null);
            await navigator.OpenWorkspaceAsync(workspace.Id);
        }
        catch (AtlasValidationException ex)
        {
            NewWorkspaceError = ValidationText.Describe(ex.Errors[0]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspace could not be created");
            NewWorkspaceError = UiText.Format(Strings.SaveFailed, services.Paths.LogsDirectory);
        }
        finally
        {
            _isCreating = false;
        }
    }
}
