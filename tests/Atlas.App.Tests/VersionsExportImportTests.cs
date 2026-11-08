using Atlas.App.ViewModels;
using Atlas.Core.Exchange;
using Atlas.Core.History;
using Atlas.Core.Resources;
using Atlas.Core.Versions;
using Atlas.Core.Workspaces;

namespace Atlas.App.Tests;

public sealed class VersionsExportImportTests : IDisposable
{
    private const string Space = "\u00A0";

    private readonly TestServices _services = new();
    private readonly TemporaryDataFolder _files = new();

    public void Dispose() => _files.Dispose();

    [Fact]
    public async Task Versions_AreShownWithTheirDateAndWhetherAtlasSavedThem()
    {
        var workspace = Seed();
        _services.Versions.Versions.Add(new WorkspaceVersion(Guid.NewGuid(), workspace.Id, "Avant le retour", _services.Clock.GetUtcNow(), true, 2, "{}"));

        var page = await OpenAsync(workspace);

        var version = Assert.Single(page.Versions);
        Assert.Equal(("Avant le retour", true, "2 ressources", "Aujourd'hui à 12:00"), (version.Name, version.IsAutomatic, version.ResourceCountText, version.SavedText));
        Assert.Equal("Rétablir la version Avant le retour", version.RestoreLabel);
        Assert.False(page.HasNoVersions);
    }

    [Fact]
    public async Task SaveVersion_SuggestsADatedNameAndIncludesNotesJustTyped()
    {
        var workspace = Seed();
        var page = await OpenAsync(workspace);
        page.Notes = "Écrit juste avant";
        _services.Dialogs.FillEditor = _ => { };

        await page.SaveVersionCommand.ExecuteAsync(null);

        Assert.Equal("Version du 6 novembre 2026 à 12:00", Assert.Single(_services.Dialogs.Editors).Name);
        var saved = Assert.Single(_services.Versions.Versions);
        Assert.Equal("Écrit juste avant", WorkspaceDocument.Read(saved.Content).Content!.Notes);
        Assert.Single(page.Versions);
    }

    [Fact]
    public async Task SaveVersion_CancelledSavesNothing()
    {
        var page = await OpenAsync(Seed());

        await page.SaveVersionCommand.ExecuteAsync(null);

        Assert.Empty(_services.Versions.Versions);
    }

    [Fact]
    public async Task SaveVersion_RefusesAnEmptyNameInTheForm()
    {
        var page = await OpenAsync(Seed());
        _services.Dialogs.FillEditor = form => form.Name = " ";

        await page.SaveVersionCommand.ExecuteAsync(null);

        Assert.Equal("Donnez un nom à cette version.", _services.Dialogs.LastEditor?.NameError);
        Assert.Empty(_services.Versions.Versions);
    }

    [Fact]
    public async Task RestoreVersion_AsksAndThenShowsTheVersionWithTheCurrentStateKept()
    {
        var workspace = Seed();
        await _services.Pages.Versions.SaveAsync(workspace.Id, "Propre", TestContext.Current.CancellationToken);
        var page = await OpenAsync(workspace);
        page.Notes = "Brouillon à garder";
        _services.Dialogs.ConfirmAnswer = true;

        await page.Versions.Single().RestoreCommand.ExecuteAsync(null);

        var question = Assert.Single(_services.Dialogs.Confirmations);
        Assert.Equal($"Rétablir «{Space}Propre{Space}»{Space}?", question.Title);
        Assert.Contains("d'abord enregistré", question.Message, StringComparison.Ordinal);
        Assert.False(question.IsDestructive);
        Assert.Equal(string.Empty, page.Notes);
        Assert.False(page.HasUnsavedChanges);
        var safety = page.Versions.Single(v => v.IsAutomatic);
        Assert.Equal($"Avant le retour à «{Space}Propre{Space}»", safety.Name);
        var kept = _services.Versions.Versions.Single(v => v.IsAutomatic);
        Assert.Equal("Brouillon à garder", WorkspaceDocument.Read(kept.Content).Content!.Notes);
    }

    [Fact]
    public async Task RestoreVersion_DeclinedChangesNothing()
    {
        var workspace = Seed();
        await _services.Pages.Versions.SaveAsync(workspace.Id, "Propre", TestContext.Current.CancellationToken);
        var page = await OpenAsync(workspace);
        page.Notes = "À garder";
        _services.Dialogs.ConfirmAnswer = false;

        await page.Versions.Single().RestoreCommand.ExecuteAsync(null);

        Assert.Equal("À garder", page.Notes);
        Assert.Single(_services.Versions.Versions);
    }

    [Fact]
    public async Task RestoreVersion_ThatCannotBeReadIsReportedAndChangesNothing()
    {
        var workspace = Seed();
        _services.Versions.Versions.Add(new WorkspaceVersion(Guid.NewGuid(), workspace.Id, "Abîmée", _services.Clock.GetUtcNow(), false, 0, "{ abîmé"));
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = true;

        await page.Versions.Single().RestoreCommand.ExecuteAsync(null);

        Assert.Contains("n'a pas changé", page.ActionError, StringComparison.Ordinal);
        Assert.Equal("Études d'été", page.Name);
        Assert.Single(_services.Versions.Versions);
    }

    [Fact]
    public async Task Delete_SaysHowManyVersionsGoWithTheWorkspace()
    {
        var workspace = Seed();
        await _services.Pages.Versions.SaveAsync(workspace.Id, "Propre", TestContext.Current.CancellationToken);
        var page = await OpenAsync(workspace);

        await page.DeleteCommand.ExecuteAsync(null);

        var message = Assert.Single(_services.Dialogs.Confirmations).Message;
        Assert.Contains("1 version enregistrée", message, StringComparison.Ordinal);
        Assert.Contains("L'historique garde la trace", message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Approve_ShowsWhereTheResourcePointsAndRecordsTheApproval()
    {
        var workspace = Workspace.Import(
            ImportPlan.From(new DocumentContent("Importé", string.Empty, string.Empty, [new DocumentResource("application", "Outil", @"\\serveur\outils\outil.exe", false)])),
            _services.Clock.GetUtcNow());
        _services.Repository.Seed(workspace);
        var page = await OpenAsync(workspace);
        var item = page.ResourceGroups[0].Items[0];
        Assert.True(item.NeedsApproval);
        _services.Dialogs.ConfirmAnswer = true;

        await item.ApproveCommand.ExecuteAsync(null);

        var detail = Assert.Single(Assert.Single(_services.Dialogs.Confirmations).Details);
        Assert.Equal(("Outil", @"\\serveur\outils\outil.exe"), (detail.Title, detail.Description));
        Assert.False(page.ResourceGroups[0].Items[0].NeedsApproval);
        Assert.Equal(HistoryAction.ResourceApproved, _services.Repository.History[^1].Action);
    }

    [Fact]
    public async Task Approve_DeclinedLeavesTheResourceWaiting()
    {
        var workspace = Workspace.Import(
            ImportPlan.From(new DocumentContent("Importé", string.Empty, string.Empty, [new DocumentResource("application", "Outil", @"C:\outil.exe", false)])),
            _services.Clock.GetUtcNow());
        _services.Repository.Seed(workspace);
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = false;

        await page.ResourceGroups[0].Items[0].ApproveCommand.ExecuteAsync(null);

        Assert.True(_services.Repository.Stored(workspace.Id)!.Resources[0].RequiresReview);
    }

    [Fact]
    public async Task Restore_IsRecordedInTheHistoryWithWhatHappened()
    {
        var workspace = Seed();
        _services.Opener.Answers[@"C:\absent.txt"] = Atlas.Core.Restoration.OpenResult.NotFound();
        await _services.Workspaces.AddResourceAsync(workspace.Id, ResourceKind.File, "Absent", @"C:\absent.txt", TestContext.Current.CancellationToken);
        var page = await OpenAsync(workspace);

        await page.RestoreCommand.ExecuteAsync(null);

        var entry = _services.Repository.History[^1];
        Assert.Equal(HistoryAction.WorkspaceRestored, entry.Action);
        Assert.Equal(new RestoreCounts(1, 1, 0, 0), entry.Counts);
    }

    [Fact]
    public async Task Export_WritesAFileAtlasCanReadBackAndRecordsIt()
    {
        var workspace = Seed();
        var page = await OpenAsync(workspace);
        var path = Path.Combine(_files.Paths.DataDirectory, "Études d'été.json");
        _services.Files.NextPath = path;

        await page.ExportCommand.ExecuteAsync(null);

        Assert.Equal(["Études d'été.json"], _services.Files.SuggestedNames);
        Assert.Empty(_services.Dialogs.Confirmations);
        var content = WorkspaceDocument.Read(await File.ReadAllTextAsync(path, TestContext.Current.CancellationToken)).Content!;
        Assert.Equal(("Études d'été", "Lien"), (content.Name, content.Resources[0].Name));
        Assert.Contains(path, page.ActionInfo, StringComparison.Ordinal);
        Assert.Equal((HistoryAction.WorkspaceExported, "Études d'été.json"), (_services.Repository.History[^1].Action, _services.Repository.History[^1].Detail));
    }

    [Fact]
    public async Task Export_WarnsWhenPathsRevealTheUserName()
    {
        var workspace = Seed();
        await _services.Workspaces.AddResourceAsync(
            workspace.Id,
            ResourceKind.Folder,
            "Documents",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Documents"),
            TestContext.Current.CancellationToken);
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = false;

        await page.ExportCommand.ExecuteAsync(null);

        var question = Assert.Single(_services.Dialogs.Confirmations);
        Assert.Contains(Environment.UserName, question.Message, StringComparison.Ordinal);
        Assert.Empty(_services.Files.Requests);
        Assert.DoesNotContain(_services.Repository.History, e => e.Action == HistoryAction.WorkspaceExported);
    }

    [Fact]
    public async Task Export_CancelledWritesNothing()
    {
        var page = await OpenAsync(Seed());

        await page.ExportCommand.ExecuteAsync(null);

        Assert.Null(page.ActionInfo);
        Assert.DoesNotContain(_services.Repository.History, e => e.Action == HistoryAction.WorkspaceExported);
    }

    [Fact]
    public async Task Export_ThatFailsSaysNothingWasWritten()
    {
        var page = await OpenAsync(Seed());
        _services.Files.NextPath = Path.Combine(_files.Paths.DataDirectory, "absent", "espace.json");

        await page.ExportCommand.ExecuteAsync(null);

        Assert.Contains("aucun fichier", page.ActionError, StringComparison.Ordinal);
        Assert.Null(page.ActionInfo);
    }

    [Fact]
    public async Task Import_CancelledShowsNothing()
    {
        await ImportAsync(path: null, answer: true);

        Assert.Empty(_services.Dialogs.ImportPreviews);
        Assert.Equal(0, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task Import_OfAFileThatIsNotAWorkspaceExplainsWhyAndCreatesNothing()
    {
        var path = await WriteAsync("pas du JSON");

        await ImportAsync(path, answer: true);

        var preview = Assert.Single(_services.Dialogs.ImportPreviews);
        Assert.False(preview.CanImport);
        Assert.Equal("Ce fichier n'est pas un JSON valide (ligne 1).", preview.Problem);
        Assert.Equal(0, _services.Repository.WriteCount);
        Assert.Empty(_services.Navigator.Visits);
    }

    [Fact]
    public async Task Import_OfAWorkspaceThatBreaksARuleCreatesNothingEvenIfConfirmed()
    {
        var path = await WriteAsync("""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "   " } }""");

        var page = await ImportAsync(path, answer: true);

        var preview = Assert.Single(_services.Dialogs.ImportPreviews);
        Assert.False(preview.CanImport);
        Assert.Equal("Donnez un nom à cet espace de travail.", preview.Problem);
        Assert.Equal(0, _services.Repository.WriteCount);
        Assert.Empty(_services.Navigator.Visits);

        // Nothing was attempted, so nothing failed either.
        Assert.Null(page.ImportError);
    }

    [Fact]
    public async Task Import_ShowsWhatWillHappenToEachResource()
    {
        var path = await WriteAsync(Document(
            """{ "kind": "url", "name": "Lien", "target": "https://example.org" }""",
            """{ "kind": "application", "name": "Outil", "target": "C:\\Outils\\outil.exe" }""",
            """{ "kind": "file", "name": "Script", "target": "C:\\Outils\\lancer.bat" }""",
            """{ "kind": "carte-mentale", "name": "Plan", "target": "plan.mm" }"""));

        await ImportAsync(path, answer: false);

        var preview = Assert.Single(_services.Dialogs.ImportPreviews);
        Assert.True(preview.CanImport);
        Assert.Equal("Espace partagé", preview.WorkspaceName);
        Assert.Equal("3 ressources sur 4 seront importées.", preview.CountText);
        Assert.Equal(
            [
                ("Lien", "Lien", null),
                ("Outil", "Application", "À approuver avant d'être ouverte"),
                ("Script", "Fichier", $"Ne sera pas importée{Space}: Ce type de fichier peut exécuter du code{Space}: Atlas ne l'ouvre pas comme un fichier. Pour lancer un programme, ajoutez-le comme application."),
                ("Plan", "carte-mentale", $"Type inconnu{Space}: conservée, mais jamais ouverte"),
            ],
            preview.Items.Select(i => (i.Name, i.KindText, i.Note)));
        Assert.Equal(0, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task Import_ConfirmedCreatesANewWorkspaceAndOpensIt()
    {
        var path = await WriteAsync(Document(
            """{ "kind": "url", "name": "Lien", "target": "https://example.org" }""",
            """{ "kind": "application", "name": "Outil", "target": "C:\\Outils\\outil.exe", "requiresReview": false }"""));

        await ImportAsync(path, answer: true);

        var summary = Assert.Single(await _services.Workspaces.ListAsync(TestContext.Current.CancellationToken));
        var workspace = await _services.Workspaces.GetAsync(summary.Id, TestContext.Current.CancellationToken);
        Assert.Equal("Espace partagé", workspace.Name);
        Assert.Equal([false, true], workspace.Resources.Select(r => r.RequiresReview));
        Assert.Equal([summary.Id.ToString()], _services.Navigator.Visits);
        Assert.Equal((HistoryAction.WorkspaceImported, "partagé.json"), (_services.Repository.History[^1].Action, _services.Repository.History[^1].Detail));
    }

    [Fact]
    public async Task Import_ThatFailsToSaveSaysSo()
    {
        var path = await WriteAsync(Document());
        _services.Repository.WriteFailure = new IOException("Disque plein");

        var page = await ImportAsync(path, answer: true);

        Assert.Contains("rien n'a été créé", page.ImportError, StringComparison.Ordinal);
        Assert.Empty(_services.Navigator.Visits);
    }

    private Workspace Seed()
    {
        var workspace = Workspace.Create("Études d'été", "Mémoire", _services.Clock.GetUtcNow());
        workspace.AddResource(ResourceKind.Url, "Lien", "https://example.org", _services.Clock.GetUtcNow());
        _services.Repository.Seed(workspace);
        return workspace;
    }

    private async Task<WorkspaceDetailViewModel> OpenAsync(Workspace workspace)
    {
        var page = new WorkspaceDetailViewModel(workspace.Id, _services.Pages, _services.Navigator);
        await page.LoadAsync();
        return page;
    }

    private async Task<WorkspacesPageViewModel> ImportAsync(string? path, bool answer)
    {
        var page = new WorkspacesPageViewModel(_services.Pages, _services.Navigator);
        _services.Files.NextPath = path;
        _services.Dialogs.ImportAnswer = answer;
        await page.ImportCommand.ExecuteAsync(null);
        return page;
    }

    private async Task<string> WriteAsync(string text)
    {
        var path = Path.Combine(_files.Paths.DataDirectory, "partagé.json");
        await File.WriteAllTextAsync(path, text, TestContext.Current.CancellationToken);
        return path;
    }

    private static string Document(params string[] resources) =>
        $$"""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Espace partagé", "resources": [ {{string.Join(",", resources)}} ] } }""";
}
