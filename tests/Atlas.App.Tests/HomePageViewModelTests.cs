using Atlas.App.ViewModels;
using Atlas.Core.Workspaces;

namespace Atlas.App.Tests;

public sealed class HomePageViewModelTests
{
    private readonly TestServices _services = new();
    private readonly HomePageViewModel _page;

    public HomePageViewModelTests() => _page = new HomePageViewModel(_services.Pages, _services.Navigator);

    [Fact]
    public void BeforeLoading_TheCountIsAnnouncedAsLoading() =>
        Assert.Equal("Chargement…", _page.WorkspaceCountText);

    [Theory]
    [InlineData(0, "0 espace de travail")]
    [InlineData(1, "1 espace de travail")]
    [InlineData(2, "2 espaces de travail")]
    public async Task LoadAsync_ShowsTheCountWithTheRightPlural(int count, string expected)
    {
        for (var i = 0; i < count; i++)
            _services.SeedWorkspace($"Espace {i}");

        await _page.LoadAsync();

        Assert.Equal(expected, _page.WorkspaceCountText);
    }

    [Fact]
    public async Task LoadAsync_InvitesToCreateTheFirstWorkspaceWhenThereIsNone()
    {
        await _page.LoadAsync();

        Assert.True(_page.IsEmpty);
        Assert.False(_page.HasRecents);
    }

    [Fact]
    public async Task LoadAsync_ListsAtMostFiveRecentWorkspacesMostRecentFirst()
    {
        for (var i = 1; i <= 7; i++)
        {
            _services.SeedWorkspace($"Espace {i}");
            _services.Clock.Advance(TimeSpan.FromMinutes(1));
        }

        await _page.LoadAsync();

        Assert.False(_page.IsEmpty);
        Assert.Equal(["Espace 7", "Espace 6", "Espace 5", "Espace 4", "Espace 3"], _page.Recents.Select(r => r.Name));
    }

    [Fact]
    public async Task LoadAsync_PutsARecentlyRestoredWorkspaceFirst()
    {
        var restored = Workspace.Create("Restauré hier", null, _services.Clock.GetUtcNow());
        _services.Clock.Advance(TimeSpan.FromHours(1));
        _services.SeedWorkspace("Modifié avant");
        _services.Clock.Advance(TimeSpan.FromHours(1));
        restored.MarkUsed(_services.Clock.GetUtcNow());
        _services.Repository.Seed(restored);

        await _page.LoadAsync();

        Assert.Equal("Restauré hier", _page.Recents[0].Name);
    }

    [Fact]
    public async Task LoadAsync_SaysSoWhenTheWorkspacesCannotBeRead()
    {
        _services.Repository.ReadFailure = new IOException("Disque indisponible");

        await _page.LoadAsync();

        Assert.Contains("Impossible de lire", _page.LoadError, StringComparison.Ordinal);
        Assert.Contains(TestServices.LogsMarker, _page.LoadError, StringComparison.Ordinal);
        Assert.Equal(string.Empty, _page.WorkspaceCountText);
        Assert.False(_page.IsEmpty);
    }

    [Fact]
    public async Task LoadCommand_RetriesAfterAFailure()
    {
        _services.SeedWorkspace();
        _services.Repository.ReadFailure = new IOException("Disque indisponible");
        await _page.LoadAsync();
        _services.Repository.ReadFailure = null;

        await _page.LoadCommand.ExecuteAsync(null);

        Assert.Null(_page.LoadError);
        Assert.Equal("1 espace de travail", _page.WorkspaceCountText);
    }

    [Fact]
    public async Task RecentWorkspace_OpensOnRequest()
    {
        var workspace = _services.SeedWorkspace();
        await _page.LoadAsync();

        await _page.Recents[0].OpenCommand.ExecuteAsync(null);

        Assert.Equal([workspace.Id.ToString()], _services.Navigator.Visits);
    }

    [Fact]
    public async Task QuickCreate_SavesTheWorkspaceAndOpensIt()
    {
        _page.NewWorkspaceName = "  Études d'été  ";

        await _page.QuickCreateCommand.ExecuteAsync(null);

        var summary = Assert.Single(await _services.Workspaces.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Études d'été", summary.Name);
        Assert.Equal([summary.Id.ToString()], _services.Navigator.Visits);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task QuickCreate_AsksForANameAndWritesNothing(string name)
    {
        _page.NewWorkspaceName = name;

        await _page.QuickCreateCommand.ExecuteAsync(null);

        Assert.Equal("Donnez un nom à cet espace de travail.", _page.NewWorkspaceError);
        Assert.Equal(0, _services.Repository.WriteCount);
        Assert.Empty(_services.Navigator.Visits);
    }

    [Fact]
    public async Task QuickCreate_RefusesATooLongNameAndKeepsWhatWasTyped()
    {
        var name = new string('é', WorkspaceRules.NameMaxLength + 1);
        _page.NewWorkspaceName = name;

        await _page.QuickCreateCommand.ExecuteAsync(null);

        Assert.Contains("120", _page.NewWorkspaceError, StringComparison.Ordinal);
        Assert.Equal(name, _page.NewWorkspaceName);
    }

    [Fact]
    public async Task QuickCreate_ExplainsAFailedSaveAndKeepsWhatWasTyped()
    {
        _services.Repository.WriteFailure = new IOException("Disque plein");
        _page.NewWorkspaceName = "Études";

        await _page.QuickCreateCommand.ExecuteAsync(null);

        Assert.Contains(TestServices.LogsMarker, _page.NewWorkspaceError, StringComparison.Ordinal);
        Assert.Equal("Études", _page.NewWorkspaceName);
        Assert.Empty(_services.Navigator.Visits);
    }

    [Fact]
    public async Task QuickCreate_ClearsThePreviousErrorOnSuccess()
    {
        await _page.QuickCreateCommand.ExecuteAsync(null);
        _page.NewWorkspaceName = "Études";

        await _page.QuickCreateCommand.ExecuteAsync(null);

        Assert.Null(_page.NewWorkspaceError);
    }

    [Fact]
    public async Task QuickCreate_RequestedTwiceAtOnceCreatesOneWorkspace()
    {
        _page.NewWorkspaceName = "Études";
        var gate = new TaskCompletionSource();
        _services.Repository.WriteGate = gate.Task;

        var first = _page.QuickCreateCommand.ExecuteAsync(null);
        var second = _page.QuickCreateCommand.ExecuteAsync(null);
        gate.SetResult();
        await Task.WhenAll(first, second);

        Assert.Equal(1, _services.Repository.WriteCount);
    }
}
