using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.Core.Detection;
using Atlas.Core.History;
using Atlas.Core.Notifications;
using Atlas.Core.Resources;

namespace Atlas.App.Tests;

/// <summary>The detection as the user meets it: consent first, then suggestions, notifications and decisions.</summary>
public sealed class SuggestionTests : IDisposable
{
    private const string Code = @"C:\Program Files\Microsoft VS Code\Code.exe";
    private const string Root = @"C:\Users\Léa\source\Atlas";

    private readonly TestServices _services = new();

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private SuggestionCenter Center => _services.Suggestions;

    public void Dispose() => Center.Dispose();

    private static WorkObservation ProjectOpenInCode(string root = Root) =>
        new(
            [new ObservedApplication("Visual Studio Code", Code, [new ObservedItem(root, true), new ObservedItem(root + @"\README.md", false)])],
            [],
            [new ObservedProject(root, Path.GetFileName(root), null, "https://github.com/ELPO-software/Atlas")]);

    private static WorkObservation ProjectInExplorerOnly() =>
        new([], [Root], [new ObservedProject(Root, "Atlas", null, "https://github.com/ELPO-software/Atlas")]);

    /// <summary>Lets the scan started by the loop finish.</summary>
    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(condition(), "The expected state was not reached.");
    }

    /// <summary>Observes the project twice, as two scans of the loop do, so that it is worth announcing.</summary>
    private async Task ObserveTwiceAsync(WorkObservation observation)
    {
        _services.Observer.Next = observation;
        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => _services.Observer.Observations == 1 && !Center.IsScanning);

        _services.Clock.Advance(SuggestionCenter.ScanInterval);
        await WaitUntilAsync(() => _services.Observer.Observations == 2 && !Center.IsScanning);
    }

    [Fact]
    public async Task NothingIsObservedBeforeTheUserAgrees()
    {
        await Center.StartAsync();
        _services.Clock.Advance(TimeSpan.FromHours(1));

        Assert.Null(Center.IsEnabled);
        Assert.Equal(0, _services.Observer.Observations);
    }

    [Fact]
    public async Task AgreeingStartsTheObservationAndIsRemembered()
    {
        _services.Observer.Next = ProjectOpenInCode();

        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => Center.Count == 1);

        Assert.True(Center.IsEnabled);
        Assert.Equal("on", await _services.SettingsStore.GetAsync("detection", Token));
        Assert.Equal("Atlas", Center.Suggestions[0].Proposal.Title);
    }

    [Fact]
    public async Task DecliningObservesNothingAndClearsTheNotifications()
    {
        await Center.SetEnabledAsync(false);
        _services.Clock.Advance(TimeSpan.FromHours(1));

        Assert.False(Center.IsEnabled);
        Assert.Equal(0, _services.Observer.Observations);
        Assert.Equal(1, _services.Notifications.RemovedAll);
    }

    [Fact]
    public async Task ACertainEnvironmentIsAnnouncedByAWindowsNotificationWhenAtlasIsInTheBackground()
    {
        await ObserveTwiceAsync(ProjectOpenInCode());

        var shown = Assert.Single(_services.Notifications.Shown);
        Assert.Equal(Strings.NotificationDetectedTitle, shown.Title);
        Assert.Contains("Atlas", shown.Message, StringComparison.Ordinal);
        Assert.Contains("Visual Studio Code", shown.Message, StringComparison.Ordinal);
        Assert.Equal(["save", "later", "ignore"], shown.Actions.Select(a => a.Argument));
        Assert.Null(Center.Announcement);
    }

    [Fact]
    public async Task AProbableEnvironmentIsNeverWordedAsACertainty()
    {
        await ObserveTwiceAsync(ProjectInExplorerOnly());

        var shown = Assert.Single(_services.Notifications.Shown);
        Assert.Equal(Strings.NotificationProbableTitle, shown.Title);
        Assert.Equal(["review", "later", "ignore"], shown.Actions.Select(a => a.Argument));
        Assert.DoesNotContain(shown.Actions, a => a.Argument == "save");
    }

    [Fact]
    public async Task WithoutWindowsNotificationsTheBannerOfAtlasAnnouncesIt()
    {
        _services.Notifications.IsAvailable = false;

        await ObserveTwiceAsync(ProjectOpenInCode());

        Assert.Empty(_services.Notifications.Shown);
        Assert.Equal("Atlas", Center.Announcement?.Proposal.Title);
    }

    [Fact]
    public async Task TurningNotificationsOffKeepsTheBannerOnly()
    {
        await Center.SetNotificationsEnabledAsync(false);

        await ObserveTwiceAsync(ProjectOpenInCode());

        Assert.Empty(_services.Notifications.Shown);
        Assert.NotNull(Center.Announcement);
        Assert.Equal("off", await _services.SettingsStore.GetAsync("detection-notifications", Token));
    }

    [Fact]
    public async Task OnlyOneAnnouncementForTheSameEnvironment()
    {
        await ObserveTwiceAsync(ProjectOpenInCode());

        for (var i = 0; i < 3; i++)
        {
            _services.Clock.Advance(SuggestionCenter.ScanInterval);
            await WaitUntilAsync(() => _services.Observer.Observations == 3 + i && !Center.IsScanning);
        }

        Assert.Single(_services.Notifications.Shown);
    }

    [Fact]
    public async Task SavingFromTheNotificationCreatesTheWorkspaceAndShowsIt()
    {
        await ObserveTwiceAsync(ProjectOpenInCode());
        var tag = _services.Notifications.Shown[0].Tag;
        Guid? opened = null;
        Center.WorkspaceSaved += (_, id) => opened = id;

        await Center.HandleActivationAsync(new NotificationActivation(tag, "save"));

        var workspace = await _services.Workspaces.GetAsync(opened!.Value, Token);
        Assert.Equal("Atlas", workspace.Name);
        Assert.Contains(workspace.Resources, r => r.Kind == ResourceKind.Folder && r.Target == Root);
        Assert.Equal(HistoryAction.WorkspaceDetected, _services.Repository.History[^1].Action);
        Assert.Contains(tag, _services.Notifications.Removed);
        Assert.Empty(Center.Suggestions);
    }

    [Fact]
    public async Task AProbableEnvironmentIsNeverSavedFromANotification()
    {
        await ObserveTwiceAsync(ProjectInExplorerOnly());
        var tag = _services.Notifications.Shown[0].Tag;
        string? requested = null;
        Center.SuggestionsRequested += (_, key) => requested = key;

        await Center.HandleActivationAsync(new NotificationActivation(tag, "save"));

        Assert.Equal(0, await _services.Workspaces.CountAsync(Token));
        Assert.Equal(Center.Suggestions[0].Key, requested);
    }

    [Fact]
    public async Task LaterAndIgnoreFromTheNotificationAreRemembered()
    {
        await ObserveTwiceAsync(ProjectOpenInCode());
        var tag = _services.Notifications.Shown[0].Tag;

        await Center.HandleActivationAsync(new NotificationActivation(tag, "later"));
        Assert.Equal(ProposalStatus.Snoozed, Assert.Single(Center.Suggestions).Status);

        await Center.HandleActivationAsync(new NotificationActivation(tag, "ignore"));
        Assert.Empty(Center.Suggestions);
        Assert.Single(await Center.ListIgnoredAsync());
    }

    [Fact]
    public async Task ClickingTheNotificationOpensTheSuggestions()
    {
        await ObserveTwiceAsync(ProjectOpenInCode());
        string? requested = null;
        Center.SuggestionsRequested += (_, key) => requested = key;

        await Center.HandleActivationAsync(new NotificationActivation(_services.Notifications.Shown[0].Tag, string.Empty));

        Assert.Equal(Center.Suggestions[0].Key, requested);
    }

    [Fact]
    public void ClosingAtlasRemovesItsNotifications()
    {
        Center.Dispose();

        Assert.Equal(1, _services.Notifications.RemovedAll);
    }

    [Fact]
    public async Task ForgettingClearsSuggestionsButKeepsWorkspaces()
    {
        _services.Observer.Next = ProjectOpenInCode();
        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => Center.Count == 1);
        await _services.Workspaces.CreateAsync("Gardé", null, Token);

        await Center.ForgetObservationsAsync();

        Assert.Empty(Center.Suggestions);
        Assert.Empty(await _services.DetectionStore.ListAsync(Token));
        Assert.Equal(1, await _services.Workspaces.CountAsync(Token));
    }

    [Fact]
    public async Task ThePageSavesOnlyTheResourcesTheUserKeeps()
    {
        _services.Observer.Next = ProjectOpenInCode();
        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => Center.Count == 1);
        using var page = new SuggestionsPageViewModel(_services.Pages, _services.Navigator);
        await page.LoadAsync();

        var card = Assert.Single(page.Cards);
        Assert.Equal("Atlas", card.Name);
        Assert.Equal(Strings.SuggestionDetected, card.ConfidenceText);
        card.Resources.Single(r => r.Resource.Kind == ResourceKind.Url).IsSelected = false;
        card.Name = "Projet Atlas";
        await card.SaveCommand.ExecuteAsync(null);

        var workspace = (await _services.Workspaces.ListAsync(Token)).Single();
        Assert.Equal("Projet Atlas", workspace.Name);
        var saved = await _services.Workspaces.GetAsync(workspace.Id, Token);
        Assert.DoesNotContain(saved.Resources, r => r.Kind == ResourceKind.Url);
        Assert.Contains(workspace.Id.ToString(), _services.Navigator.Visits);
        Assert.Empty(page.Cards);
    }

    [Fact]
    public async Task ThePageRefusesToSaveAnEmptySelection()
    {
        _services.Observer.Next = ProjectOpenInCode();
        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => Center.Count == 1);
        using var page = new SuggestionsPageViewModel(_services.Pages, _services.Navigator);
        await page.LoadAsync();
        var card = Assert.Single(page.Cards);

        foreach (var resource in card.Resources)
            resource.IsSelected = false;
        await card.SaveCommand.ExecuteAsync(null);

        Assert.Equal(Strings.SuggestionNothingSelected, card.Error);
        Assert.Equal(0, await _services.Workspaces.CountAsync(Token));
    }

    [Fact]
    public async Task ThePageKeepsWhatTheUserUntickedWhenTheEnvironmentIsSeenAgain()
    {
        _services.Observer.Next = ProjectOpenInCode();
        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => Center.Count == 1);
        using var page = new SuggestionsPageViewModel(_services.Pages, _services.Navigator);
        await page.LoadAsync();
        var card = Assert.Single(page.Cards);
        card.Resources.Single(r => r.Resource.Kind == ResourceKind.Url).IsSelected = false;

        await page.ScanCommand.ExecuteAsync(null);

        Assert.Same(card, Assert.Single(page.Cards));
        Assert.False(card.Resources.Single(r => r.Resource.Kind == ResourceKind.Url).IsSelected);
    }

    [Fact]
    public async Task IgnoringFromThePageCanBeTakenBack()
    {
        _services.Observer.Next = ProjectOpenInCode();
        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => Center.Count == 1);
        using var page = new SuggestionsPageViewModel(_services.Pages, _services.Navigator);
        await page.LoadAsync();

        await page.Cards[0].IgnoreCommand.ExecuteAsync(null);
        Assert.Empty(page.Cards);
        var ignored = Assert.Single(page.Ignored);

        await ignored.RestoreCommand.ExecuteAsync(null);
        Assert.Single(page.Cards);
        Assert.Empty(page.Ignored);
    }

    [Fact]
    public async Task ThePageAsksBeforeObservingAnything()
    {
        await Center.StartAsync();
        using var page = new SuggestionsPageViewModel(_services.Pages, _services.Navigator);
        await page.LoadAsync();

        Assert.True(page.IsUndecided);
        Assert.False(page.IsEnabled);

        await page.EnableCommand.ExecuteAsync(null);

        Assert.True(page.IsEnabled);
        Assert.False(page.IsUndecided);
    }

    [Fact]
    public async Task TheNavigationCountsTheSuggestionsWaiting()
    {
        var main = new MainWindowViewModel(_services.Pages);
        Assert.False(main.HasSuggestionBadge);
        Assert.Equal(Strings.NavSuggestions, main.SuggestionsNavigationLabel);

        _services.Observer.Next = ProjectOpenInCode();
        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => Center.Count == 1);

        Assert.True(main.HasSuggestionBadge);
        Assert.Equal("1", main.SuggestionBadgeText);
        Assert.Contains("1", main.SuggestionsNavigationLabel, StringComparison.Ordinal);
        main.Dispose();
    }

    [Fact]
    public async Task TheBannerSavesACertainEnvironmentAndOpensIt()
    {
        _services.Notifications.IsAvailable = false;
        var main = new MainWindowViewModel(_services.Pages);
        await ObserveTwiceAsync(ProjectOpenInCode());
        Assert.True(main.HasAnnouncement);
        Assert.Equal(Strings.SuggestionSave, main.AnnouncementAcceptLabel);

        await main.AcceptAnnouncementCommand.ExecuteAsync(null);

        Assert.IsType<WorkspaceDetailViewModel>(main.CurrentPage);
        Assert.Equal(1, await _services.Workspaces.CountAsync(Token));
        Assert.False(main.HasAnnouncement);
        main.Dispose();
    }

    [Fact]
    public async Task TheBannerSendsAProbableEnvironmentToReview()
    {
        _services.Notifications.IsAvailable = false;
        var main = new MainWindowViewModel(_services.Pages);
        await ObserveTwiceAsync(ProjectInExplorerOnly());
        Assert.Equal(Strings.SuggestionReview, main.AnnouncementAcceptLabel);

        await main.AcceptAnnouncementCommand.ExecuteAsync(null);

        Assert.IsType<SuggestionsPageViewModel>(main.CurrentPage);
        Assert.Equal(0, await _services.Workspaces.CountAsync(Token));
        main.Dispose();
    }

    [Fact]
    public async Task SettingsTurnTheDetectionOnAndOff()
    {
        var settings = new SettingsPageViewModel(_services.Pages);
        await settings.LoadAsync();
        Assert.False(settings.DetectionEnabled);

        settings.DetectionEnabled = true;
        await settings.PendingDetectionSave;
        Assert.True(Center.IsEnabled);

        settings.DetectionEnabled = false;
        await settings.PendingDetectionSave;
        Assert.False(Center.IsEnabled);
    }

    [Fact]
    public async Task SettingsForgetOnlyAfterConfirmation()
    {
        _services.Observer.Next = ProjectOpenInCode();
        await Center.SetEnabledAsync(true);
        await WaitUntilAsync(() => Center.Count == 1);
        var settings = new SettingsPageViewModel(_services.Pages);

        _services.Dialogs.ConfirmAnswer = false;
        await settings.ForgetObservationsCommand.ExecuteAsync(null);
        Assert.Equal(1, Center.Count);

        _services.Dialogs.ConfirmAnswer = true;
        await settings.ForgetObservationsCommand.ExecuteAsync(null);
        Assert.Equal(0, Center.Count);
        Assert.Equal(Strings.ObservationsForgotten, settings.DetectionInfo);
    }

    [Fact]
    public async Task HomeInvitesOnceThenPointsToTheSuggestions()
    {
        await Center.StartAsync();
        using var home = new HomePageViewModel(_services.Pages, _services.Navigator);
        await home.LoadAsync();
        Assert.True(home.IsDetectionUndecided);

        _services.Observer.Next = ProjectOpenInCode();
        await home.EnableDetectionCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => Center.Count == 1);

        Assert.False(home.IsDetectionUndecided);
        Assert.True(home.HasSuggestions);
        Assert.Equal(Strings.SuggestionsWaitingOne, home.SuggestionsWaitingText);
        Assert.Contains("suggestions", _services.Navigator.Visits);
    }
}
