using System.ComponentModel;
using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Core.Validation;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class HomePageViewModel : PageViewModel, IDisposable
{
    private const int RecentCount = 5;

    private readonly PageServices _services;
    private readonly INavigator _navigator;
    private readonly ILogger _logger;
    private bool _isCreating;

    public HomePageViewModel(PageServices services, INavigator navigator)
    {
        _services = services;
        _navigator = navigator;
        _logger = services.Loggers.CreateLogger<HomePageViewModel>();
        services.Suggestions.PropertyChanged += OnSuggestionsChanged;
    }

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

    /// <summary>True until the user decides whether Atlas may detect environments: the home page invites them to.</summary>
    public bool IsDetectionUndecided => _services.Suggestions.IsEnabled is null;

    public bool HasSuggestions => _services.Suggestions.IsEnabled == true && _services.Suggestions.Count > 0;

    public string SuggestionsWaitingText => _services.Suggestions.Count <= 1
        ? Strings.SuggestionsWaitingOne
        : UiText.Format(Strings.SuggestionsWaitingMany, _services.Suggestions.Count);

    [RelayCommand(AllowConcurrentExecutions = true)]
    public override async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var workspaces = await _services.Workspaces.ListAsync();

            WorkspaceCountText = UiText.WorkspaceCount(workspaces.Count);
            IsEmpty = workspaces.Count == 0;
            Recents = workspaces
                .OrderByDescending(w => w.LastActivityAt)
                .Take(RecentCount)
                .Select(w => new WorkspaceCardViewModel(w, _services.Time, _navigator))
                .ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspaces could not be read");
            WorkspaceCountText = string.Empty;
            LoadError = UiText.Format(Strings.WorkspacesReadFailed, _services.Paths.LogsDirectory);
        }
    }

    public void Dispose() => _services.Suggestions.PropertyChanged -= OnSuggestionsChanged;

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
            var workspace = await _services.Workspaces.CreateAsync(NewWorkspaceName, description: null);
            await _navigator.OpenWorkspaceAsync(workspace.Id);
        }
        catch (AtlasValidationException ex)
        {
            NewWorkspaceError = ValidationText.Describe(ex.Errors[0]);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspace could not be created");
            NewWorkspaceError = UiText.Format(Strings.SaveFailed, _services.Paths.LogsDirectory);
        }
        finally
        {
            _isCreating = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task EnableDetectionAsync() => DecideDetectionAsync(true);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task DeclineDetectionAsync() => DecideDetectionAsync(false);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task OpenSuggestionsAsync() => _navigator.OpenSuggestionsAsync();

    private async Task DecideDetectionAsync(bool enabled)
    {
        try
        {
            await _services.Suggestions.SetEnabledAsync(enabled);
            if (enabled)
                await _navigator.OpenSuggestionsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The detection setting could not be saved");
            LoadError = UiText.Format(Strings.SaveFailed, _services.Paths.LogsDirectory);
        }
    }

    private void OnSuggestionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        OnPropertyChanged(nameof(IsDetectionUndecided));
        OnPropertyChanged(nameof(HasSuggestions));
        OnPropertyChanged(nameof(SuggestionsWaitingText));
    }
}
