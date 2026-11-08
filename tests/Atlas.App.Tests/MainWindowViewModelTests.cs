using Atlas.App.ViewModels;

namespace Atlas.App.Tests;

public sealed class MainWindowViewModelTests
{
    private readonly TestServices _services = new();
    private readonly MainWindowViewModel _viewModel;

    public MainWindowViewModelTests() => _viewModel = new MainWindowViewModel(_services.Pages);

    [Fact]
    public async Task InitializeAsync_OpensTheHomePage()
    {
        await _viewModel.InitializeAsync();

        Assert.IsType<HomePageViewModel>(_viewModel.CurrentPage);
        Assert.True(_viewModel.IsHomeSelected);
    }

    [Fact]
    public async Task OpenWorkspacesAsync_ShowsTheListAndSelectsItsSection()
    {
        await _viewModel.OpenWorkspacesAsync();

        Assert.IsType<WorkspacesPageViewModel>(_viewModel.CurrentPage);
        Assert.True(_viewModel.IsWorkspacesSelected);
        Assert.False(_viewModel.IsHomeSelected);
    }

    [Fact]
    public async Task OpenSettingsAsync_ShowsTheSettingsAndSelectsItsSection()
    {
        await _viewModel.OpenSettingsAsync();

        Assert.IsType<SettingsPageViewModel>(_viewModel.CurrentPage);
        Assert.True(_viewModel.IsSettingsSelected);
    }

    [Fact]
    public async Task OpenWorkspaceAsync_ShowsTheWorkspaceWithinTheWorkspacesSection()
    {
        var workspace = _services.SeedWorkspace();

        await _viewModel.OpenWorkspaceAsync(workspace.Id);

        var page = Assert.IsType<WorkspaceDetailViewModel>(_viewModel.CurrentPage);
        Assert.Equal(workspace.Name, page.Name);
        Assert.True(_viewModel.IsWorkspacesSelected);
    }

    [Fact]
    public async Task Navigation_LoadsTheNewPage()
    {
        _services.SeedWorkspace();

        await _viewModel.OpenWorkspacesAsync();

        Assert.Single(Assert.IsType<WorkspacesPageViewModel>(_viewModel.CurrentPage).Cards);
    }

    [Fact]
    public async Task Navigation_StaysOnThePageWhenItRefusesToBeLeft()
    {
        var page = await OpenWorkspaceWithNotesThatCannotBeSavedAsync();
        _services.Dialogs.ConfirmAnswer = false;

        await _viewModel.OpenHomeAsync();

        Assert.Same(page, _viewModel.CurrentPage);
        Assert.True(_viewModel.IsWorkspacesSelected);
    }

    [Fact]
    public async Task Navigation_LeavesWhenTheUserAcceptsToLoseTheNotes()
    {
        await OpenWorkspaceWithNotesThatCannotBeSavedAsync();
        _services.Dialogs.ConfirmAnswer = true;

        await _viewModel.OpenHomeAsync();

        Assert.IsType<HomePageViewModel>(_viewModel.CurrentPage);
    }

    [Fact]
    public async Task Navigation_IgnoresASecondRequestWhileThePageIsFinishingItsWork()
    {
        var workspace = _services.SeedWorkspace();
        await _viewModel.OpenWorkspaceAsync(workspace.Id);
        var page = Assert.IsType<WorkspaceDetailViewModel>(_viewModel.CurrentPage);
        page.Notes = "À enregistrer";
        var gate = new TaskCompletionSource();
        _services.Repository.WriteGate = gate.Task;

        var first = _viewModel.OpenHomeAsync();
        var second = _viewModel.OpenSettingsAsync();
        await second;
        gate.SetResult();
        await first;

        Assert.IsType<HomePageViewModel>(_viewModel.CurrentPage);
    }

    [Fact]
    public async Task TryCloseAsync_SavesPendingNotes()
    {
        var workspace = _services.SeedWorkspace();
        await _viewModel.OpenWorkspaceAsync(workspace.Id);
        Assert.IsType<WorkspaceDetailViewModel>(_viewModel.CurrentPage).Notes = "Écrit juste avant de fermer";
        Assert.True(_viewModel.HasUnsavedChanges);

        Assert.True(await _viewModel.TryCloseAsync());

        Assert.Equal("Écrit juste avant de fermer", _services.Repository.Stored(workspace.Id)?.Notes);
        Assert.False(_viewModel.HasUnsavedChanges);
    }

    [Fact]
    public async Task TryCloseAsync_StaysOpenWhenNotesCannotBeSavedAndTheUserStays()
    {
        await OpenWorkspaceWithNotesThatCannotBeSavedAsync();
        _services.Dialogs.ConfirmAnswer = false;

        Assert.False(await _viewModel.TryCloseAsync());
    }

    [Fact]
    public void HasUnsavedChanges_IsFalseBeforeAnyPageOpens() =>
        Assert.False(_viewModel.HasUnsavedChanges);

    [Fact]
    public void ShowUnexpectedError_PointsToTheLogUntilDismissed()
    {
        _viewModel.ShowUnexpectedError();

        Assert.Contains(TestServices.LogsMarker, _viewModel.UnexpectedErrorText, StringComparison.Ordinal);

        _viewModel.DismissUnexpectedErrorCommand.Execute(null);

        Assert.Null(_viewModel.UnexpectedErrorText);
    }

    private async Task<WorkspaceDetailViewModel> OpenWorkspaceWithNotesThatCannotBeSavedAsync()
    {
        var workspace = _services.SeedWorkspace();
        await _viewModel.OpenWorkspaceAsync(workspace.Id);
        var page = Assert.IsType<WorkspaceDetailViewModel>(_viewModel.CurrentPage);
        _services.Repository.WriteFailure = new IOException("Disque plein");
        page.Notes = "Ne peut pas être enregistré";
        return page;
    }
}
