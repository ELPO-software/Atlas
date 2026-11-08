using Atlas.App.ViewModels;
using Atlas.Core.Resources;
using Atlas.Core.Workspaces;

namespace Atlas.App.Tests;

public sealed class WorkspacesPageViewModelTests
{
    private readonly TestServices _services = new();
    private readonly WorkspacesPageViewModel _page;

    public WorkspacesPageViewModelTests() => _page = new WorkspacesPageViewModel(_services.Pages, _services.Navigator);

    [Fact]
    public async Task LoadAsync_ShowsWhatEachCardNeeds()
    {
        var workspace = Workspace.Create("Études d'été", "Mémoire de fin d'année", _services.Clock.GetUtcNow());
        workspace.AddResource(ResourceKind.Url, "Bibliothèque", "https://example.org", _services.Clock.GetUtcNow());
        workspace.AddResource(ResourceKind.Folder, "Sources", @"C:\Données d'été", _services.Clock.GetUtcNow());
        _services.Repository.Seed(workspace);

        await _page.LoadAsync();

        var card = Assert.Single(_page.Cards);
        Assert.Equal("Études d'été", card.Name);
        Assert.Equal("Mémoire de fin d'année", card.Description);
        Assert.Equal("2 ressources", card.ResourceCountText);
        Assert.Equal("Modifié aujourd'hui à 12:00", card.ModifiedText);
        Assert.Equal("Ouvrir Études d'été", card.OpenLabel);
        Assert.Equal("1 espace de travail", _page.WorkspaceCountText);
        Assert.False(_page.IsEmpty);
    }

    [Fact]
    public async Task LoadAsync_ListsTheMostRecentlyModifiedFirst()
    {
        _services.SeedWorkspace("Ancien");
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        _services.SeedWorkspace("Récent");

        await _page.LoadAsync();

        Assert.Equal(["Récent", "Ancien"], _page.Cards.Select(c => c.Name));
    }

    [Fact]
    public async Task LoadAsync_InvitesToCreateAWorkspaceWhenThereIsNone()
    {
        await _page.LoadAsync();

        Assert.True(_page.IsEmpty);
        Assert.Empty(_page.Cards);
        Assert.Equal("0 espace de travail", _page.WorkspaceCountText);
    }

    [Fact]
    public async Task LoadAsync_SaysSoWhenTheWorkspacesCannotBeRead()
    {
        _services.Repository.ReadFailure = new IOException("Disque indisponible");

        await _page.LoadAsync();

        Assert.Contains(TestServices.LogsMarker, _page.LoadError, StringComparison.Ordinal);
        Assert.False(_page.IsEmpty);
    }

    [Fact]
    public async Task Card_OpensItsWorkspace()
    {
        var workspace = _services.SeedWorkspace();
        await _page.LoadAsync();

        await _page.Cards[0].OpenCommand.ExecuteAsync(null);

        Assert.Equal([workspace.Id.ToString()], _services.Navigator.Visits);
    }

    [Fact]
    public async Task Create_SavesWhatTheFormReceivedAndOpensTheWorkspace()
    {
        _services.Dialogs.FillEditor = form =>
        {
            form.Name = "Études d'été";
            form.Description = "Mémoire";
        };

        await _page.CreateCommand.ExecuteAsync(null);

        var summary = Assert.Single(await _services.Workspaces.ListAsync(TestContext.Current.CancellationToken));
        Assert.Equal("Études d'été", summary.Name);
        Assert.Equal("Mémoire", summary.Description);
        Assert.Equal([summary.Id.ToString()], _services.Navigator.Visits);
    }

    [Fact]
    public async Task Create_OffersAnEmptyFormWithADescription()
    {
        await _page.CreateCommand.ExecuteAsync(null);

        var form = Assert.Single(_services.Dialogs.Editors);
        Assert.Equal(string.Empty, form.Name);
        Assert.True(form.ShowDescription);
    }

    [Fact]
    public async Task Create_CancelledWritesNothingAndStaysHere()
    {
        await _page.CreateCommand.ExecuteAsync(null);

        Assert.Equal(0, _services.Repository.WriteCount);
        Assert.Empty(_services.Navigator.Visits);
    }

    [Fact]
    public async Task Create_RefusedStaysHereAndExplainsInTheForm()
    {
        _services.Dialogs.FillEditor = form => form.Name = "   ";

        await _page.CreateCommand.ExecuteAsync(null);

        Assert.Equal(0, _services.Repository.WriteCount);
        Assert.Empty(_services.Navigator.Visits);
        Assert.Equal("Donnez un nom à cet espace de travail.", _services.Dialogs.LastEditor?.NameError);
    }
}
