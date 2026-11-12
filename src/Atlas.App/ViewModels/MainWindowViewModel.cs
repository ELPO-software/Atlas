using System.ComponentModel;
using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.Text;
using Atlas.Core.Detection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public enum AppSection
{
    Home,
    Workspaces,
    Suggestions,
    History,
    Settings,
}

public sealed partial class MainWindowViewModel : ViewModelBase, INavigator, IDisposable
{
    private const int BadgeLimit = 9;

    private readonly PageServices _services;
    private readonly ILogger _logger;
    private bool _isLeavingPage;

    public MainWindowViewModel(PageServices services)
    {
        _services = services;
        _logger = services.Loggers.CreateLogger<MainWindowViewModel>();
        Suggestions.PropertyChanged += OnSuggestionsChanged;
        Suggestions.SuggestionsRequested += OnSuggestionsRequested;
        Suggestions.WorkspaceSaved += OnWorkspaceSaved;
    }

    public SuggestionCenter Suggestions => _services.Suggestions;

    [ObservableProperty]
    public partial PageViewModel? CurrentPage { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsHomeSelected), nameof(IsWorkspacesSelected), nameof(IsSuggestionsSelected), nameof(IsHistorySelected), nameof(IsSettingsSelected))]
    public partial AppSection Section { get; private set; }

    [ObservableProperty]
    public partial string? UnexpectedErrorText { get; private set; }

    [ObservableProperty]
    public partial string? AnnouncementError { get; private set; }

    public bool IsHomeSelected => Section == AppSection.Home;

    public bool IsWorkspacesSelected => Section == AppSection.Workspaces;

    public bool IsSuggestionsSelected => Section == AppSection.Suggestions;

    public bool IsHistorySelected => Section == AppSection.History;

    public bool IsSettingsSelected => Section == AppSection.Settings;

    public bool HasUnsavedChanges => CurrentPage?.HasUnsavedChanges ?? false;

    public bool HasSuggestionBadge => Suggestions.Count > 0;

    public string SuggestionBadgeText => Suggestions.Count > BadgeLimit ? $"{BadgeLimit}+" : Suggestions.Count.ToString(UiText.Culture);

    /// <summary>What a screen reader announces for the navigation item, with the number of suggestions waiting.</summary>
    public string SuggestionsNavigationLabel =>
        Suggestions.Count == 0 ? Strings.NavSuggestions : UiText.Format(Strings.NavSuggestionsWithCount, Suggestions.Count);

    public bool HasAnnouncement => Suggestions.Announcement is not null;

    public bool IsAnnouncementCertain => Suggestions.Announcement?.Proposal.Confidence == DetectionConfidence.Detected;

    public string AnnouncementTitle => IsAnnouncementCertain ? Strings.NotificationDetectedTitle : Strings.NotificationProbableTitle;

    public string AnnouncementMessage => Suggestions.Announcement is { } announced
        ? SuggestionText.Notification(announced, string.Empty).Message
        : string.Empty;

    public string AnnouncementAcceptLabel => IsAnnouncementCertain ? Strings.SuggestionSave : Strings.SuggestionReview;

    public Task InitializeAsync() => OpenHomeAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenHomeAsync() =>
        GoToAsync(new HomePageViewModel(_services, this), AppSection.Home);

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenWorkspacesAsync() =>
        GoToAsync(new WorkspacesPageViewModel(_services, this), AppSection.Workspaces);

    public Task OpenWorkspaceAsync(Guid workspaceId) =>
        GoToAsync(new WorkspaceDetailViewModel(workspaceId, _services, this), AppSection.Workspaces);

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenSuggestionsAsync(string? focusKey = null) =>
        GoToAsync(new SuggestionsPageViewModel(_services, this, focusKey), AppSection.Suggestions);

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenHistoryAsync() =>
        GoToAsync(new HistoryPageViewModel(_services), AppSection.History);

    [RelayCommand(AllowConcurrentExecutions = true)]
    public Task OpenSettingsAsync() =>
        GoToAsync(new SettingsPageViewModel(_services), AppSection.Settings);

    /// <returns>False if the user chose to stay.</returns>
    public Task<bool> TryCloseAsync() => CurrentPage?.TryLeaveAsync() ?? Task.FromResult(true);

    public void ShowUnexpectedError() =>
        UnexpectedErrorText = UiText.Format(Strings.UnexpectedError, _services.Paths.LogsDirectory);

    public void Dispose()
    {
        Suggestions.PropertyChanged -= OnSuggestionsChanged;
        Suggestions.SuggestionsRequested -= OnSuggestionsRequested;
        Suggestions.WorkspaceSaved -= OnWorkspaceSaved;
        (CurrentPage as IDisposable)?.Dispose();
    }

    [RelayCommand]
    private void DismissUnexpectedError() => UnexpectedErrorText = null;

    /// <summary>
    /// The main button of the banner: a certain environment is saved as it is and opened; a probable one is shown on
    /// the suggestions page first, so that the user checks it.
    /// </summary>
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task AcceptAnnouncementAsync()
    {
        if (Suggestions.Announcement is not { } announced)
            return;

        AnnouncementError = null;
        if (announced.Proposal.Confidence != DetectionConfidence.Detected)
        {
            Suggestions.DismissAnnouncement();
            await OpenSuggestionsAsync(announced.Key);
            return;
        }

        try
        {
            var workspace = await Suggestions.SaveAsync(announced.Key, announced.Proposal.Title, announced.Proposal.Resources);
            await OpenWorkspaceAsync(workspace.Id);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "An announced suggestion could not be saved");
            Suggestions.DismissAnnouncement();
            await OpenSuggestionsAsync(announced.Key);
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task LaterAnnouncementAsync() => DecideAnnouncementAsync(Suggestions.SnoozeAsync);

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task IgnoreAnnouncementAsync() => DecideAnnouncementAsync(Suggestions.IgnoreAsync);

    [RelayCommand]
    private void DismissAnnouncement() => Suggestions.DismissAnnouncement();

    private async Task DecideAnnouncementAsync(Func<string, Task> decide)
    {
        if (Suggestions.Announcement is not { } announced)
            return;

        AnnouncementError = null;
        try
        {
            await decide(announced.Key);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "A decision about an announced suggestion could not be saved");
            AnnouncementError = UiText.Format(Strings.SaveFailed, _services.Paths.LogsDirectory);
        }
    }

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

        var previous = CurrentPage;
        CurrentPage = page;
        Section = section;
        (previous as IDisposable)?.Dispose();
        await page.LoadAsync();
    }

    private void OnSuggestionsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(SuggestionCenter.Suggestions))
        {
            OnPropertyChanged(nameof(HasSuggestionBadge));
            OnPropertyChanged(nameof(SuggestionBadgeText));
            OnPropertyChanged(nameof(SuggestionsNavigationLabel));
        }
        else if (e.PropertyName is nameof(SuggestionCenter.Announcement))
        {
            AnnouncementError = null;
            OnPropertyChanged(nameof(HasAnnouncement));
            OnPropertyChanged(nameof(IsAnnouncementCertain));
            OnPropertyChanged(nameof(AnnouncementTitle));
            OnPropertyChanged(nameof(AnnouncementMessage));
            OnPropertyChanged(nameof(AnnouncementAcceptLabel));
        }
    }

    private void OnSuggestionsRequested(object? sender, string? key) => _ = OpenSuggestionsAsync(key);

    private void OnWorkspaceSaved(object? sender, Guid workspaceId) => _ = OpenWorkspaceAsync(workspaceId);
}
