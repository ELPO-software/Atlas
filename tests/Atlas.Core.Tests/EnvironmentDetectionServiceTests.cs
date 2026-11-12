using Atlas.Core.Detection;
using Atlas.Core.History;
using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;
using Microsoft.Extensions.Time.Testing;

namespace Atlas.Core.Tests;

public sealed class EnvironmentDetectionServiceTests
{
    private const string Code = @"C:\Program Files\Microsoft VS Code\Code.exe";
    private const string Word = @"C:\Program Files\Microsoft Office\root\Office16\WINWORD.EXE";
    private const string Terminal = @"shell:AppsFolder\Microsoft.WindowsTerminal_8wekyb3d8bbwe!App";
    private const string Root = @"C:\Users\Léa\source\Atlas";

    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 12, 9, 0, 0, TimeSpan.Zero));
    private readonly FakeObserver _observer = new();
    private readonly InMemoryDetectionStore _store = new();
    private readonly InMemoryWorkspaceRepository _repository = new();
    private readonly WorkspaceService _workspaces;
    private readonly EnvironmentDetectionService _service;

    public EnvironmentDetectionServiceTests()
    {
        _clock.SetLocalTimeZone(TimeZoneInfo.Utc);
        _workspaces = new WorkspaceService(_repository, _clock);
        _service = new EnvironmentDetectionService(_observer, _store, _workspaces, _clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static WorkObservation ProjectOpenInCode(string root = Root) =>
        new(
            [new ObservedApplication("Visual Studio Code", Code, [new ObservedItem(root, true)])],
            [],
            [new ObservedProject(root, Path.GetFileName(root), null, "https://github.com/ELPO-software/Atlas")]);

    private static WorkObservation Context(params string[] folders) =>
        new([new ObservedApplication("Word", Word, []), new ObservedApplication("Terminal", Terminal, [])], folders, []);

    [Fact]
    public async Task AProjectIsSuggestedAtOnceButNotifiedOnlyOnceItStays()
    {
        _observer.Next = ProjectOpenInCode();

        var first = await _service.ScanAsync(Token);
        Assert.Equal("Atlas", Assert.Single(first.Suggestions).Proposal.Title);
        Assert.Null(first.ToNotify);

        _clock.Advance(TimeSpan.FromMinutes(2));
        var second = await _service.ScanAsync(Token);
        Assert.Equal("Atlas", second.ToNotify?.Proposal.Title);
    }

    [Fact]
    public async Task AProjectThatIsGoneIsNotNotified()
    {
        _observer.Next = ProjectOpenInCode();
        await _service.ScanAsync(Token);

        _clock.Advance(TimeSpan.FromMinutes(2));
        _observer.Next = WorkObservation.Empty;
        var scan = await _service.ScanAsync(Token);

        Assert.Single(scan.Suggestions);
        Assert.Null(scan.ToNotify);
    }

    [Fact]
    public async Task OneNotificationAtMostInTheInterval()
    {
        _observer.Next = ProjectOpenInCode();
        await _service.ScanAsync(Token);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var scan = await _service.ScanAsync(Token);
        await _service.MarkNotifiedAsync(scan.ToNotify!.Key, Token);

        _observer.Next = ProjectOpenInCode(@"C:\Users\Léa\source\Sysora");
        await _service.ScanAsync(Token);
        _clock.Advance(TimeSpan.FromMinutes(2));
        Assert.Null((await _service.ScanAsync(Token)).ToNotify);

        _clock.Advance(EnvironmentDetectionService.NotificationInterval);
        Assert.Equal("Sysora", (await _service.ScanAsync(Token)).ToNotify?.Proposal.Title);
    }

    [Fact]
    public async Task ANotifiedEnvironmentIsNotNotifiedAgain()
    {
        _observer.Next = ProjectOpenInCode();
        await _service.ScanAsync(Token);
        _clock.Advance(TimeSpan.FromMinutes(2));
        await _service.MarkNotifiedAsync((await _service.ScanAsync(Token)).ToNotify!.Key, Token);

        _clock.Advance(TimeSpan.FromHours(3));
        var scan = await _service.ScanAsync(Token);

        Assert.Null(scan.ToNotify);
        Assert.Single(scan.Suggestions);
    }

    [Fact]
    public async Task AnIgnoredEnvironmentIsNeverSuggestedAgain()
    {
        _observer.Next = ProjectOpenInCode();
        var key = Assert.Single((await _service.ScanAsync(Token)).Suggestions).Key;

        await _service.IgnoreAsync(key, Token);
        _clock.Advance(TimeSpan.FromDays(3));
        var scan = await _service.ScanAsync(Token);

        Assert.Empty(scan.Suggestions);
        Assert.Null(scan.ToNotify);
        Assert.Equal(key, Assert.Single(await _service.ListIgnoredAsync(Token)).Key);
    }

    [Fact]
    public async Task TakingBackAnIgnoreBringsTheSuggestionBackWithoutANotification()
    {
        _observer.Next = ProjectOpenInCode();
        var key = Assert.Single((await _service.ScanAsync(Token)).Suggestions).Key;
        await _service.IgnoreAsync(key, Token);

        await _service.RestoreIgnoredAsync(key, Token);
        _clock.Advance(TimeSpan.FromMinutes(5));
        var scan = await _service.ScanAsync(Token);

        Assert.Single(scan.Suggestions);
        Assert.Null(scan.ToNotify);
    }

    [Fact]
    public async Task LaterKeepsTheSuggestionQuietUntilItsTime()
    {
        _observer.Next = ProjectOpenInCode();
        await _service.ScanAsync(Token);
        _clock.Advance(TimeSpan.FromMinutes(2));
        var key = (await _service.ScanAsync(Token)).ToNotify!.Key;
        await _service.MarkNotifiedAsync(key, Token);
        await _service.SnoozeAsync(key, Token);

        _clock.Advance(TimeSpan.FromHours(1));
        var during = await _service.ScanAsync(Token);
        Assert.Equal(ProposalStatus.Snoozed, Assert.Single(during.Suggestions).Status);
        Assert.Null(during.ToNotify);

        _clock.Advance(EnvironmentDetectionService.SnoozeDuration);
        var after = await _service.ScanAsync(Token);
        Assert.Equal(key, after.ToNotify?.Key);
    }

    [Fact]
    public async Task SavingCreatesTheWorkspaceWithTheChosenResourcesOnly()
    {
        _observer.Next = ProjectOpenInCode();
        var suggestion = Assert.Single((await _service.ScanAsync(Token)).Suggestions);
        var kept = suggestion.Proposal.Resources.Where(r => r.Kind != ResourceKind.Url).ToList();

        var workspace = await _service.SaveAsync(suggestion.Key, "Projet Atlas", kept, Token);

        var saved = await _workspaces.GetAsync(workspace.Id, Token);
        Assert.Equal("Projet Atlas", saved.Name);
        Assert.Equal(kept.Select(r => (r.Kind, r.Target)), saved.Resources.Select(r => (r.Kind, r.Target)));
        Assert.Equal(HistoryAction.WorkspaceDetected, Assert.Single(_repository.History).Action);
        Assert.Empty((await _service.ScanAsync(Token)).Suggestions);
    }

    [Fact]
    public async Task SavingRefusesAResourceThatWasNotProposed()
    {
        _observer.Next = ProjectOpenInCode();
        var suggestion = Assert.Single((await _service.ScanAsync(Token)).Suggestions);
        var foreign = new ProposedResource(ResourceKind.Folder, "Autre", @"C:\Windows", ProposalReason.ProjectFolder);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.SaveAsync(suggestion.Key, "Projet", [foreign], Token));

        Assert.Equal(0, await _workspaces.CountAsync(Token));
    }

    [Fact]
    public async Task SavingWithARefusedNameSavesNothing()
    {
        _observer.Next = ProjectOpenInCode();
        var suggestion = Assert.Single((await _service.ScanAsync(Token)).Suggestions);

        await Assert.ThrowsAsync<AtlasValidationException>(() => _service.SaveAsync(suggestion.Key, "  ", suggestion.Proposal.Resources, Token));

        Assert.Equal(0, await _workspaces.CountAsync(Token));
        Assert.Single(await _service.ListSuggestionsAsync(Token));
    }

    [Fact]
    public async Task AProjectAlreadyInAWorkspaceIsNotSuggested()
    {
        var existing = await _workspaces.CreateAsync("Mon Atlas", null, Token);
        await _workspaces.AddResourceAsync(existing.Id, ResourceKind.Folder, "Atlas", @"c:\users\léa\source\atlas\", Token);
        _observer.Next = ProjectOpenInCode();

        var scan = await _service.ScanAsync(Token);

        Assert.Empty(scan.Suggestions);
    }

    [Fact]
    public async Task AContextIsSuggestedOnlyOnceSeenOnAnotherDay()
    {
        _observer.Next = Context(@"D:\Cours", @"D:\Notes");
        Assert.Empty((await _service.ScanAsync(Token)).Suggestions);

        _clock.Advance(TimeSpan.FromHours(3));
        Assert.Empty((await _service.ScanAsync(Token)).Suggestions);

        _clock.Advance(TimeSpan.FromDays(1));
        _observer.Next = Context(@"D:\Cours", @"D:\Notes", @"D:\Brouillons");
        var scan = await _service.ScanAsync(Token);

        var context = Assert.Single(scan.Suggestions);
        Assert.Equal(ProposalKind.RecurringContext, context.Proposal.Kind);
        Assert.Equal(2, context.SeenDays);
        Assert.Equal(context.Key, scan.ToNotify?.Key);
    }

    [Fact]
    public async Task ADifferentContextStartsItsOwnCount()
    {
        _observer.Next = Context(@"D:\Cours", @"D:\Notes");
        await _service.ScanAsync(Token);

        _clock.Advance(TimeSpan.FromDays(1));
        _observer.Next = new WorkObservation([new ObservedApplication("Photos", @"C:\Apps\Photos.exe", [])], [@"E:\Images", @"E:\Vacances"], []);

        Assert.Empty((await _service.ScanAsync(Token)).Suggestions);
    }

    [Fact]
    public async Task UndecidedEnvironmentsAreForgottenAfterAWhile()
    {
        _observer.Next = ProjectOpenInCode();
        await _service.ScanAsync(Token);

        _clock.Advance(EnvironmentDetectionService.ForgetAfter + TimeSpan.FromDays(1));
        _observer.Next = WorkObservation.Empty;
        await _service.ScanAsync(Token);

        Assert.Empty(await _store.ListAsync(Token));
    }

    [Fact]
    public async Task ForgettingKeepsOnlyTheRecordOfSavedEnvironments()
    {
        _observer.Next = ProjectOpenInCode();
        var suggestion = Assert.Single((await _service.ScanAsync(Token)).Suggestions);
        await _service.SaveAsync(suggestion.Key, "Atlas", suggestion.Proposal.Resources, Token);
        _observer.Next = Context(@"D:\Cours", @"D:\Notes");
        await _service.ScanAsync(Token);

        await _service.ForgetObservationsAsync(Token);

        Assert.Equal(ProposalStatus.Saved, Assert.Single(await _store.ListAsync(Token)).Status);
    }

    private sealed class FakeObserver : IWorkObserver
    {
        public WorkObservation Next { get; set; } = WorkObservation.Empty;

        public Task<WorkObservation> ObserveAsync(CancellationToken cancellationToken = default) => Task.FromResult(Next);
    }
}

internal sealed class InMemoryDetectionStore : IDetectionStore
{
    private readonly Dictionary<string, StoredProposal> _proposals = new(StringComparer.Ordinal);

    public Task<IReadOnlyList<StoredProposal>> ListAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<StoredProposal>>([.. _proposals.Values]);

    public Task SaveAsync(StoredProposal proposal, CancellationToken cancellationToken = default)
    {
        _proposals[proposal.Key] = proposal;
        return Task.CompletedTask;
    }

    public Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        foreach (var key in keys)
            _proposals.Remove(key);

        return Task.CompletedTask;
    }
}
