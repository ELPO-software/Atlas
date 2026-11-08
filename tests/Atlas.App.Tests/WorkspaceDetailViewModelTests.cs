using Atlas.App.ViewModels;
using Atlas.Core.Resources;
using Atlas.Core.Workspaces;

namespace Atlas.App.Tests;

public sealed class WorkspaceDetailViewModelTests
{
    private readonly TestServices _services = new();

    [Fact]
    public async Task LoadAsync_ShowsTheDetailsAndTheNotes()
    {
        var workspace = Workspace.Create("Études d'été", "Mémoire", _services.Clock.GetUtcNow());
        workspace.UpdateNotes("À relire", _services.Clock.GetUtcNow());
        _services.Repository.Seed(workspace);

        var page = await OpenAsync(workspace);

        Assert.True(page.IsLoaded);
        Assert.Equal("Études d'été", page.Name);
        Assert.Equal("Mémoire", page.Description);
        Assert.Equal("À relire", page.Notes);
        Assert.Equal("Créé le 6 novembre 2026", page.CreatedText);
        Assert.Equal("Modifié aujourd'hui à 12:00", page.ModifiedText);
    }

    [Fact]
    public async Task LoadAsync_WritesNothingAndHasNothingToSave()
    {
        var workspace = Workspace.Create("Études", null, _services.Clock.GetUtcNow());
        workspace.UpdateNotes("À relire", _services.Clock.GetUtcNow());
        _services.Repository.Seed(workspace);

        var page = await OpenAsync(workspace);

        Assert.False(page.HasUnsavedChanges);
        Assert.Null(page.NotesStatus);
        _services.Clock.Advance(TimeSpan.FromMinutes(1));
        await page.PendingNotesSave;
        Assert.Equal(0, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task LoadAsync_GroupsResourcesByCategoryInAFixedOrder()
    {
        var now = _services.Clock.GetUtcNow();
        var workspace = Workspace.Create("Études", null, now);
        workspace.AddResource(ResourceKind.Url, "Bibliothèque", "https://example.org", now);
        workspace.AddResource(ResourceKind.Parse("carte-mentale"), "Plan", "plan.mm", now);
        workspace.AddResource(ResourceKind.Folder, "Sources", @"C:\Données d'été", now);
        workspace.AddResource(ResourceKind.Application, "Éditeur", @"C:\Programmes\Éditeur.exe", now);
        workspace.AddResource(ResourceKind.File, "Mémoire", @"C:\Données d'été\mémoire.docx", now);
        workspace.AddResource(ResourceKind.File, "Annexes", @"C:\Données d'été\annexes.pdf", now);
        _services.Repository.Seed(workspace);

        var page = await OpenAsync(workspace);

        Assert.False(page.HasNoResources);
        Assert.Equal(
            ["Applications", "Fichiers", "Dossiers", "Liens", "Autres types"],
            page.ResourceGroups.Select(g => g.Title));
        Assert.Equal(
            [("Mémoire", @"C:\Données d'été\mémoire.docx"), ("Annexes", @"C:\Données d'été\annexes.pdf")],
            page.ResourceGroups[1].Items.Select(i => (i.Name, i.Target)));
    }

    [Fact]
    public async Task LoadAsync_SaysWhenThereAreNoResources()
    {
        var page = await OpenAsync(_services.SeedWorkspace());

        Assert.True(page.HasNoResources);
        Assert.Empty(page.ResourceGroups);
    }

    [Fact]
    public async Task LoadAsync_SaysSoWhenTheWorkspaceNoLongerExists()
    {
        var page = new WorkspaceDetailViewModel(Guid.NewGuid(), _services.Pages, _services.Navigator);

        await page.LoadAsync();

        Assert.False(page.IsLoaded);
        Assert.Equal("Cet espace de travail n'existe plus.", page.LoadError);
    }

    [Fact]
    public async Task LoadAsync_SaysSoWhenTheWorkspaceCannotBeRead()
    {
        var workspace = _services.SeedWorkspace();
        _services.Repository.ReadFailure = new IOException("Disque indisponible");
        var page = new WorkspaceDetailViewModel(workspace.Id, _services.Pages, _services.Navigator);

        await page.LoadAsync();

        Assert.False(page.IsLoaded);
        Assert.Contains(TestServices.LogsMarker, page.LoadError, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Back_ReturnsToTheList()
    {
        var page = await OpenAsync(_services.SeedWorkspace());

        await page.BackCommand.ExecuteAsync(null);

        Assert.Equal(["workspaces"], _services.Navigator.Visits);
    }

    [Fact]
    public async Task Notes_AreNotSavedBeforeThePauseInTyping()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);

        page.Notes = "Brouillon";
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay - TimeSpan.FromMilliseconds(1));

        Assert.Equal(string.Empty, _services.Repository.Stored(workspace.Id)?.Notes);
        Assert.Equal("Enregistrement…", page.NotesStatus);
        Assert.True(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Notes_AreSavedAfterThePauseAndSaySo()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);

        page.Notes = "À relire : chapitre 3";
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        Assert.Equal("À relire : chapitre 3", _services.Repository.Stored(workspace.Id)?.Notes);
        Assert.Equal("Enregistré", page.NotesStatus);
        Assert.False(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Notes_EachKeystrokeRestartsThePause()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);

        page.Notes = "É";
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay / 2);
        page.Notes = "Ét";
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay / 2);

        Assert.Equal(0, _services.Repository.WriteCount);

        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay / 2);
        await page.PendingNotesSave;

        Assert.Equal("Ét", _services.Repository.Stored(workspace.Id)?.Notes);
        Assert.Equal(1, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task Notes_TypedDuringASaveAreStoredRightAfterIt()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        var gate = new TaskCompletionSource();
        _services.Repository.WriteGate = gate.Task;

        page.Notes = "Premier";
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        var firstSave = page.PendingNotesSave;
        page.Notes = "Premier et second";
        gate.SetResult();
        await firstSave;

        Assert.Equal("Premier et second", _services.Repository.Stored(workspace.Id)?.Notes);
        Assert.False(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Notes_SavingUpdatesTheModificationDate()
    {
        var page = await OpenAsync(_services.SeedWorkspace());
        _services.Clock.Advance(TimeSpan.FromHours(1));

        page.Notes = "Plus tard";
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        Assert.Equal("Modifié aujourd'hui à 13:00", page.ModifiedText);
    }

    [Fact]
    public async Task Notes_ThatReturnToTheStoredTextEndAsSavedWithoutWriting()
    {
        var page = await OpenAsync(_services.SeedWorkspace());

        page.Notes = "Faute de frappe";
        page.Notes = string.Empty;
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        Assert.Equal("Enregistré", page.NotesStatus);
        Assert.Equal(0, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task Notes_AFailedSaveIsReportedAndTheTextStaysOnScreen()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        _services.Repository.WriteFailure = new IOException("Disque plein");

        page.Notes = "À ne pas perdre";
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        Assert.Contains("n'ont pas pu être enregistrées", page.NotesError, StringComparison.Ordinal);
        Assert.Contains(TestServices.LogsMarker, page.NotesError, StringComparison.Ordinal);
        Assert.Null(page.NotesStatus);
        Assert.Equal("À ne pas perdre", page.Notes);
        Assert.True(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Notes_RetrySavesAfterAFailure()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        _services.Repository.WriteFailure = new IOException("Disque plein");
        page.Notes = "À ne pas perdre";
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;
        _services.Repository.WriteFailure = null;

        await page.RetrySaveNotesCommand.ExecuteAsync(null);

        Assert.Equal("À ne pas perdre", _services.Repository.Stored(workspace.Id)?.Notes);
        Assert.Null(page.NotesError);
        Assert.Equal("Enregistré", page.NotesStatus);
    }

    [Fact]
    public async Task Notes_TooLongAreRefusedWithAClearMessageAndNotTruncated()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        var text = new string('é', WorkspaceRules.NotesMaxLength + 1);

        page.Notes = text;
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        Assert.Contains("ne sont pas enregistrées", page.NotesError, StringComparison.Ordinal);
        Assert.Equal(text, page.Notes);
        Assert.Equal(string.Empty, _services.Repository.Stored(workspace.Id)?.Notes);
    }

    [Fact]
    public async Task TryLeave_SavesWhatIsPendingWithoutWaitingForThePause()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        page.Notes = "Écrit juste avant de partir";

        Assert.True(await page.TryLeaveAsync());

        Assert.Equal("Écrit juste avant de partir", _services.Repository.Stored(workspace.Id)?.Notes);
        Assert.Empty(_services.Dialogs.Confirmations);
    }

    [Fact]
    public async Task TryLeave_DoesNotSaveAgainAfterThePauseWouldHaveEnded()
    {
        var page = await OpenAsync(_services.SeedWorkspace());
        page.Notes = "Une seule écriture";
        await page.TryLeaveAsync();

        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        Assert.Equal(1, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task TryLeave_WritesNothingWhenNothingChanged()
    {
        var page = await OpenAsync(_services.SeedWorkspace());

        Assert.True(await page.TryLeaveAsync());

        Assert.Equal(0, _services.Repository.WriteCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TryLeave_AsksBeforeLosingNotesThatCannotBeSaved(bool userAccepts)
    {
        var page = await OpenAsync(_services.SeedWorkspace());
        _services.Repository.WriteFailure = new IOException("Disque plein");
        _services.Dialogs.ConfirmAnswer = userAccepts;
        page.Notes = "À ne pas perdre";

        var left = await page.TryLeaveAsync();

        Assert.Equal(userAccepts, left);
        var question = Assert.Single(_services.Dialogs.Confirmations);
        Assert.Contains("seront perdues", question.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryLeave_LeavesAPageThatCouldNotLoadWithoutAsking()
    {
        var page = new WorkspaceDetailViewModel(Guid.NewGuid(), _services.Pages, _services.Navigator);
        await page.LoadAsync();

        Assert.True(await page.TryLeaveAsync());
        Assert.Empty(_services.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Edit_SavesAndShowsTheNewDetails()
    {
        var workspace = _services.SeedWorkspace("Avant");
        var page = await OpenAsync(workspace);
        _services.Dialogs.FillEditor = form =>
        {
            form.Name = "Après";
            form.Description = "Décrit";
        };

        await page.EditCommand.ExecuteAsync(null);

        Assert.Equal("Après", page.Name);
        Assert.Equal("Décrit", page.Description);
        Assert.Equal("Après", _services.Repository.Stored(workspace.Id)?.Name);
    }

    [Fact]
    public async Task Edit_StartsFromTheCurrentValues()
    {
        var page = await OpenAsync(_services.SeedWorkspace("Études", "Mémoire"));

        await page.EditCommand.ExecuteAsync(null);

        var form = Assert.Single(_services.Dialogs.Editors);
        Assert.Equal("Études", form.Name);
        Assert.Equal("Mémoire", form.Description);
    }

    [Fact]
    public async Task Edit_CancelledChangesNothing()
    {
        var page = await OpenAsync(_services.SeedWorkspace("Avant"));

        await page.EditCommand.ExecuteAsync(null);

        Assert.Equal("Avant", page.Name);
        Assert.Equal(0, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task Edit_DoesNotOverwriteNotesBeingTyped()
    {
        var workspace = _services.SeedWorkspace("Avant");
        var page = await OpenAsync(workspace);
        page.Notes = "En cours";
        _services.Dialogs.FillEditor = form => form.Name = "Après";

        await page.EditCommand.ExecuteAsync(null);

        Assert.Equal("En cours", page.Notes);
        Assert.True(page.HasUnsavedChanges);

        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        var stored = _services.Repository.Stored(workspace.Id);
        Assert.Equal("Après", stored?.Name);
        Assert.Equal("En cours", stored?.Notes);
    }

    [Fact]
    public async Task Duplicate_SuggestsANameAndOpensTheCopy()
    {
        var workspace = _services.SeedWorkspace("Études");
        var page = await OpenAsync(workspace);
        _services.Dialogs.FillEditor = _ => { };

        await page.DuplicateCommand.ExecuteAsync(null);

        var form = Assert.Single(_services.Dialogs.Editors);
        Assert.Equal("Études (copie)", form.Name);
        Assert.False(form.ShowDescription);
        var copy = (await _services.Workspaces.ListAsync(TestContext.Current.CancellationToken)).Single(w => w.Id != workspace.Id);
        Assert.Equal("Études (copie)", copy.Name);
        Assert.Equal([copy.Id.ToString()], _services.Navigator.Visits);
    }

    [Fact]
    public async Task Duplicate_ShortensTheSuggestionToFitTheLimit()
    {
        var page = await OpenAsync(_services.SeedWorkspace(new string('é', WorkspaceRules.NameMaxLength)));
        _services.Dialogs.FillEditor = _ => { };

        await page.DuplicateCommand.ExecuteAsync(null);

        var suggestion = Assert.Single(_services.Dialogs.Editors).Name;
        Assert.Equal(WorkspaceRules.NameMaxLength, suggestion.Length);
        Assert.EndsWith(" (copie)", suggestion, StringComparison.Ordinal);
        Assert.Equal(1, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task Duplicate_IncludesNotesTypedJustBefore()
    {
        var workspace = _services.SeedWorkspace("Études");
        var page = await OpenAsync(workspace);
        page.Notes = "Écrit juste avant";
        _services.Dialogs.FillEditor = _ => { };

        await page.DuplicateCommand.ExecuteAsync(null);

        var copyId = (await _services.Workspaces.ListAsync(TestContext.Current.CancellationToken)).Single(w => w.Id != workspace.Id).Id;
        Assert.Equal("Écrit juste avant", _services.Repository.Stored(copyId)?.Notes);
    }

    [Fact]
    public async Task Duplicate_CancelledCreatesNothing()
    {
        var page = await OpenAsync(_services.SeedWorkspace());

        await page.DuplicateCommand.ExecuteAsync(null);

        Assert.Equal(1, await _services.Workspaces.CountAsync(TestContext.Current.CancellationToken));
        Assert.Empty(_services.Navigator.Visits);
    }

    [Fact]
    public async Task Delete_AsksFirstAndNamesWhatWillBeRemoved()
    {
        var now = _services.Clock.GetUtcNow();
        var workspace = Workspace.Create("Études d'été", null, now);
        workspace.AddResource(ResourceKind.Url, "Lien", "https://example.org", now);
        workspace.AddResource(ResourceKind.Url, "Autre lien", "https://example.com", now);
        _services.Repository.Seed(workspace);
        var page = await OpenAsync(workspace);

        await page.DeleteCommand.ExecuteAsync(null);

        var question = Assert.Single(_services.Dialogs.Confirmations);
        Assert.Contains("Études d'été", question.Message, StringComparison.Ordinal);
        Assert.Contains("2 ressources", question.Message, StringComparison.Ordinal);
        Assert.Contains("ne sont pas touchés", question.Message, StringComparison.Ordinal);
        Assert.Equal("Supprimer", question.ConfirmLabel);
        Assert.Equal("Annuler", question.CancelLabel);
    }

    [Fact]
    public async Task Delete_RefusedKeepsTheWorkspace()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = false;

        await page.DeleteCommand.ExecuteAsync(null);

        Assert.NotNull(_services.Repository.Stored(workspace.Id));
        Assert.Empty(_services.Navigator.Visits);
    }

    [Fact]
    public async Task Delete_ConfirmedRemovesTheWorkspaceAndReturnsToTheList()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = true;

        await page.DeleteCommand.ExecuteAsync(null);

        Assert.Null(_services.Repository.Stored(workspace.Id));
        Assert.Equal(["workspaces"], _services.Navigator.Visits);
        Assert.False(page.HasUnsavedChanges);
    }

    [Fact]
    public async Task Delete_ConfirmedDropsNotesThatWereWaitingToBeSaved()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = true;
        page.Notes = "Sans objet";

        await page.DeleteCommand.ExecuteAsync(null);
        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        Assert.Null(page.NotesError);
        Assert.Equal(1, _services.Repository.WriteCount);
        Assert.True(await page.TryLeaveAsync());
        Assert.Single(_services.Dialogs.Confirmations);
    }

    [Fact]
    public async Task Delete_FailedKeepsTheWorkspaceReportsItAndStaysHere()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = true;
        _services.Repository.WriteFailure = new IOException("Disque plein");

        await page.DeleteCommand.ExecuteAsync(null);

        Assert.Contains("conservé", page.ActionError, StringComparison.Ordinal);
        Assert.NotNull(_services.Repository.Stored(workspace.Id));
        Assert.Empty(_services.Navigator.Visits);
    }

    [Fact]
    public async Task Delete_FailedStillSavesNotesThatWereWaiting()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = true;
        page.Notes = "À garder";
        _services.Repository.WriteFailure = new IOException("Disque plein");
        await page.DeleteCommand.ExecuteAsync(null);
        _services.Repository.WriteFailure = null;

        _services.Clock.Advance(WorkspaceDetailViewModel.NotesSaveDelay);
        await page.PendingNotesSave;

        Assert.Equal("À garder", _services.Repository.Stored(workspace.Id)?.Notes);
    }

    [Fact]
    public async Task Delete_OfAWorkspaceAlreadyGoneSimplyReturnsToTheList()
    {
        var workspace = _services.SeedWorkspace();
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = true;
        await _services.Workspaces.DeleteAsync(workspace.Id, TestContext.Current.CancellationToken);

        await page.DeleteCommand.ExecuteAsync(null);

        Assert.Null(page.ActionError);
        Assert.Equal(["workspaces"], _services.Navigator.Visits);
    }

    private async Task<WorkspaceDetailViewModel> OpenAsync(Workspace workspace)
    {
        var page = new WorkspaceDetailViewModel(workspace.Id, _services.Pages, _services.Navigator);
        await page.LoadAsync();
        return page;
    }
}
