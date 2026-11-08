using Atlas.App.Resources;
using Atlas.App.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Atlas.App.ViewModels;

public enum AppSection
{
    Home,
    Workspaces,
    Settings,
}

public sealed partial class MainWindowViewModel(PageServices services) : ViewModelBase, INavigator
{
    private bool _isLeavingPage;

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected), nameof(IsWorkspacesSelected), nameof(IsSettingsSelected))]
    public partial AppSection Section { get; private set; }

    [ObservableProperty]
    public partial string? UnexpectedErrorText { get; private set; }

    public bool IsHomeSelected => Section == AppSection.Home;

    public bool IsWorkspacesSelected => Section == AppSection.Workspaces;

    public bool IsSettingsSelected => Section == AppSection.Settings;

    public bool HasUnsavedChanges => CurrentPage?.HasUnsavedChanges ?? false;

    public Task InitializeAsync() => OpenHomeAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenHomeAsync() =>
        GoToAsync(new HomePageViewModel(services, this), AppSection.Home);

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenWorkspacesAsync() =>
        GoToAsync(new WorkspacesPageViewModel(services, this), AppSection.Workspaces);

    public Task OpenWorkspaceAsync(Guid workspaceId) =>
        GoToAsync(new WorkspaceDetailViewModel(workspaceId, services, this), AppSection.Workspaces);

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenSettingsAsync() =>
        GoToAsync(new SettingsPageViewModel(services), AppSection.Settings);

    /// <returns>False if the user chose to stay.</returns>
    public Task<bool> TryCloseAsync() => CurrentPage?.TryLeaveAsync() ?? Task.FromResult(true);

    public void ShowUnexpectedError() =>
        UnexpectedErrorText = UiText.Format(Strings.UnexpectedError, services.Paths.LogsDirectory);

    [RelayCommand]
    private void DismissUnexpectedError() => UnexpectedErrorText = null;

    private async Task GoToAsync(PageViewModel page, AppSection section)
    {
        // A second click while the current page is still finishing its work must not start another departure.
        if (_isLeavingPage)
            return;

        _isLeavingPage = true;
        try
        {
            if (CurrentPage is { } current && !await current.TryLeaveAsync())
                return;
        }
        finally
        {
            _isLeavingPage = false;
        }

        CurrentPage = page;
        Section = section;
        await page.LoadAsync();
    }
}
