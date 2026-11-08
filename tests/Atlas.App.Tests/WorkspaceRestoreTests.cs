using Atlas.App.ViewModels;
using Atlas.Core.Resources;
using Atlas.Core.Restoration;
using Atlas.Core.Workspaces;

namespace Atlas.App.Tests;

/// <summary>Restoring and managing resources from the workspace page. Nothing is opened for real.</summary>
public sealed class WorkspaceRestoreTests
{
    private const string Calculator = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    private readonly TestServices _services = new();

    [Fact]
    public async Task Restore_IsUnavailableWithoutResources()
    {
        var page = await OpenAsync(Seed());

        Assert.False(page.CanRestore);
        await page.RestoreCommand.ExecuteAsync(null);
        Assert.Empty(_services.Opener.Opened);
    }

    [Fact]
    public async Task Restore_OpensEverythingInOrderAndReportsIt()
    {
        var workspace = Seed(
            (ResourceKind.Url, "Bibliothèque", "https://example.org"),
            (ResourceKind.File, "Mémoire", @"C:\Données d'été\mémoire.docx"),
            (ResourceKind.Application, "Éditeur", @"C:\Programmes\Éditeur.exe"));
        var page = await OpenAsync(workspace);

        await page.RestoreCommand.ExecuteAsync(null);

        Assert.Equal([@"C:\Programmes\Éditeur.exe", @"C:\Données d'été\mémoire.docx", "https://example.org"], _services.Opener.Opened);
        Assert.Empty(_services.Dialogs.Confirmations);
        var report = Assert.IsType<RestoreReportViewModel>(page.Report);
        Assert.Equal("Restauration terminée", report.Title);
        Assert.Equal("3 restaurées", report.Summary);
        Assert.Equal(["Lancée", "Ouverte", "Ouverte"], report.Lines.Select(l => l.Status));
        Assert.False(page.IsRestoring);
    }

    [Fact]
    public async Task Restore_RecordsTheUseWithoutChangingTheModificationDate()
    {
        var workspace = Seed((ResourceKind.Url, "Lien", "https://example.org"));
        var page = await OpenAsync(workspace);
        _services.Clock.Advance(TimeSpan.FromHours(2));

        await page.RestoreCommand.ExecuteAsync(null);

        var stored = _services.Repository.Stored(workspace.Id)!;
        Assert.Equal(_services.Clock.GetUtcNow(), stored.LastUsedAt);
        Assert.Equal(workspace.UpdatedAt, stored.UpdatedAt);
    }

    [Fact]
    public async Task Restore_AsksBeforeStartingSeveralApplicationsAndListsThem()
    {
        var page = await OpenAsync(Seed(
            (ResourceKind.Application, "Éditeur", @"C:\Programmes\Éditeur.exe"),
            (ResourceKind.Application, "Calculatrice", Calculator),
            (ResourceKind.File, "Mémoire", @"C:\a\mémoire.docx")));
        _services.Dialogs.ConfirmAnswer = true;

        await page.RestoreCommand.ExecuteAsync(null);

        var question = Assert.Single(_services.Dialogs.Confirmations);
        Assert.Equal("Lancer 2 applications\u00A0?", question.Title);
        Assert.False(question.IsDestructive);
        Assert.Equal(
            [("Éditeur", @"C:\Programmes\Éditeur.exe"), ("Calculatrice", "Application installée sur cet ordinateur")],
            question.Details.Select(d => (d.Title, d.Description)));
        Assert.Contains("ne sont pas fermées", question.Message, StringComparison.Ordinal);
        Assert.Equal(3, _services.Opener.Opened.Count);
    }

    [Fact]
    public async Task Restore_StartsNothingWhenTheUserDeclines()
    {
        var workspace = Seed(
            (ResourceKind.Application, "Un", @"C:\a\un.exe"),
            (ResourceKind.Application, "Deux", @"C:\a\deux.exe"));
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = false;

        await page.RestoreCommand.ExecuteAsync(null);

        Assert.Empty(_services.Opener.Opened);
        Assert.Null(page.Report);
        Assert.Null(_services.Repository.Stored(workspace.Id)!.LastUsedAt);
    }

    [Fact]
    public async Task Restore_ReportsProblemsHonestlyAndStandsThemOut()
    {
        var workspace = Workspace.Restore(
            Guid.NewGuid(),
            "Importé",
            string.Empty,
            string.Empty,
            _services.Clock.GetUtcNow(),
            _services.Clock.GetUtcNow(),
            null,
            [
                Resource(ResourceKind.File, "Absent", @"C:\a\absent.txt"),
                Resource(ResourceKind.File, "Script", @"C:\a\lancer.bat"),
                Resource(ResourceKind.Folder, "Partage", @"\\serveur\partage", requiresReview: true),
                Resource(ResourceKind.File, "Fragile", @"C:\a\fragile.xyz"),
            ]);
        _services.Repository.Seed(workspace);
        _services.Opener.Answers[@"C:\a\absent.txt"] = OpenResult.NotFound();
        _services.Opener.Answers[@"C:\a\fragile.xyz"] = OpenResult.Failed(RestoreReason.SystemRefused, "Accès refusé.");
        var page = await OpenAsync(workspace);

        await page.RestoreCommand.ExecuteAsync(null);

        var report = page.Report!;
        Assert.Equal("1 introuvable, 1 en erreur, 2 ignorées", report.Summary);
        var lines = report.Lines.ToDictionary(l => l.Name);
        Assert.Equal("Introuvable", lines["Absent"].Status);
        Assert.True(lines["Absent"].IsProblem);
        Assert.Equal("Ignorée", lines["Script"].Status);
        Assert.Contains("exécuter du code", lines["Script"].Detail, StringComparison.Ordinal);
        Assert.False(lines["Script"].IsProblem);
        Assert.Contains("approuvée", lines["Partage"].Detail, StringComparison.Ordinal);
        Assert.Equal("Windows a refusé de l'ouvrir\u00A0: Accès refusé.", lines["Fragile"].Detail);
        Assert.Equal([@"C:\a\absent.txt", @"C:\a\fragile.xyz"], _services.Opener.Opened);
    }

    [Fact]
    public async Task Restore_BlocksChangesWhileItRuns()
    {
        var page = await OpenAsync(Seed((ResourceKind.Url, "Lien", "https://example.org")));
        var hold = new TaskCompletionSource();
        _services.Opener.Hold = hold.Task;

        var restoring = page.RestoreCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _services.Opener.Opened.Count == 1);

        Assert.True(page.IsRestoring);
        Assert.False(page.CanChange);
        Assert.False(page.CanRestore);
        Assert.True(page.HasUnsavedChanges);

        hold.SetResult();
        await restoring;
        Assert.True(page.CanChange);
    }

    [Fact]
    public async Task Restore_RequestedTwiceAtOnceRunsOnce()
    {
        var page = await OpenAsync(Seed((ResourceKind.Url, "Lien", "https://example.org")));
        var hold = new TaskCompletionSource();
        _services.Opener.Hold = hold.Task;

        var first = page.RestoreCommand.ExecuteAsync(null);
        var second = page.RestoreCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _services.Opener.Opened.Count == 1);
        hold.SetResult();
        await Task.WhenAll(first, second);

        Assert.Single(_services.Opener.Opened);
    }

    [Fact]
    public async Task Stop_LeavesWhatIsOpenAndSkipsTheRest()
    {
        var page = await OpenAsync(Seed(
            (ResourceKind.File, "Un", @"C:\a\un.txt"),
            (ResourceKind.File, "Deux", @"C:\a\deux.txt")));
        var hold = new TaskCompletionSource();
        _services.Opener.Hold = hold.Task;

        var restoring = page.RestoreCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _services.Opener.Opened.Count == 1);
        page.StopRestoreCommand.Execute(null);
        hold.SetResult();
        await restoring;

        var report = page.Report!;
        Assert.Equal("Restauration arrêtée", report.Title);
        Assert.Equal(["Ouverte", "Ignorée"], report.Lines.Select(l => l.Status));
        Assert.Contains("arrêtée avant", report.Lines[1].Detail, StringComparison.Ordinal);
        Assert.Single(_services.Opener.Opened);
    }

    [Fact]
    public async Task Leaving_DuringARestorationAsksFirstAndCanStay()
    {
        var page = await OpenAsync(Seed((ResourceKind.File, "Un", @"C:\a\un.txt"), (ResourceKind.File, "Deux", @"C:\a\deux.txt")));
        var hold = new TaskCompletionSource();
        _services.Opener.Hold = hold.Task;
        var restoring = page.RestoreCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _services.Opener.Opened.Count == 1);
        _services.Dialogs.ConfirmAnswer = false;

        Assert.False(await page.TryLeaveAsync());
        Assert.True(page.IsRestoring);

        hold.SetResult();
        await restoring;
        Assert.Equal(2, _services.Opener.Opened.Count);
    }

    [Fact]
    public async Task Leaving_DuringARestorationStopsItWhenTheUserAgrees()
    {
        var page = await OpenAsync(Seed((ResourceKind.File, "Un", @"C:\a\un.txt"), (ResourceKind.File, "Deux", @"C:\a\deux.txt")));
        var hold = new TaskCompletionSource();
        _services.Opener.Hold = hold.Task;
        var restoring = page.RestoreCommand.ExecuteAsync(null);
        await WaitUntilAsync(() => _services.Opener.Opened.Count == 1);
        _services.Dialogs.ConfirmAnswer = true;

        var leaving = page.TryLeaveAsync();
        hold.SetResult();

        Assert.True(await leaving);
        await restoring;
        Assert.Single(_services.Opener.Opened);
        Assert.Contains("Arrêter", Assert.Single(_services.Dialogs.Confirmations).Title, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Restore_ExplainsWhenTheWorkspaceCannotBeRead()
    {
        var page = await OpenAsync(Seed((ResourceKind.Url, "Lien", "https://example.org")));
        _services.Repository.ReadFailure = new IOException("Disque indisponible");

        await page.RestoreCommand.ExecuteAsync(null);

        Assert.Contains(TestServices.LogsMarker, page.ActionError, StringComparison.Ordinal);
        Assert.Empty(_services.Opener.Opened);
    }

    [Fact]
    public async Task Restore_StillReportsWhenItsUseCannotBeRecorded()
    {
        var page = await OpenAsync(Seed((ResourceKind.Url, "Lien", "https://example.org")));
        _services.Repository.WriteFailure = new IOException("Disque plein");

        await page.RestoreCommand.ExecuteAsync(null);

        Assert.Equal("1 restaurée", page.Report?.Summary);
        Assert.Null(page.ActionError);
    }

    [Fact]
    public async Task CloseReport_HidesIt()
    {
        var page = await OpenAsync(Seed((ResourceKind.Url, "Lien", "https://example.org")));
        await page.RestoreCommand.ExecuteAsync(null);

        page.CloseReportCommand.Execute(null);

        Assert.False(page.HasReport);
    }

    [Fact]
    public async Task AddResource_AsksForTheTypeAndShowsTheNewResource()
    {
        var workspace = Seed();
        var page = await OpenAsync(workspace);
        _services.Clock.Advance(TimeSpan.FromHours(1));
        _services.Dialogs.FillResourceEditor = form =>
        {
            form.IsFolder = true;
            form.Target = @"C:\Données d'été\Sources";
        };

        await page.AddResourceCommand.ExecuteAsync(null);

        Assert.Null(Assert.Single(_services.Dialogs.ResourceEditors).FixedKind);
        var item = Assert.Single(Assert.Single(page.ResourceGroups).Items);
        Assert.Equal(("Sources", @"C:\Données d'été\Sources"), (item.Name, item.Target));
        Assert.True(page.CanRestore);
        Assert.Equal("Modifié aujourd'hui à 13:00", page.ModifiedText);
        Assert.Single(_services.Repository.Stored(workspace.Id)!.Resources);
    }

    [Fact]
    public async Task AddResource_CancelledChangesNothing()
    {
        var page = await OpenAsync(Seed());

        await page.AddResourceCommand.ExecuteAsync(null);

        Assert.True(page.HasNoResources);
        Assert.Equal(0, _services.Repository.WriteCount);
    }

    [Fact]
    public async Task EditResource_KeepsItsTypeAndStartsFromItsValues()
    {
        var workspace = Seed((ResourceKind.File, "Mémoire", @"C:\a\mémoire.docx"));
        var page = await OpenAsync(workspace);
        _services.Dialogs.FillResourceEditor = form => form.DisplayName = "Mémoire final";

        await page.ResourceGroups[0].Items[0].EditCommand.ExecuteAsync(null);

        var request = Assert.Single(_services.Dialogs.ResourceEditors);
        Assert.Equal(ResourceKind.File, request.FixedKind);
        Assert.Equal(("Mémoire", @"C:\a\mémoire.docx"), (request.DisplayName, request.Target));
        Assert.Equal("Mémoire final", page.ResourceGroups[0].Items[0].Name);
        Assert.Equal("Mémoire final", _services.Repository.Stored(workspace.Id)!.Resources[0].DisplayName);
    }

    [Fact]
    public async Task RemoveResource_AsksFirstAndSaysTheFileIsNotTouched()
    {
        var workspace = Seed((ResourceKind.File, "Mémoire", @"C:\a\mémoire.docx"));
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = false;

        await page.ResourceGroups[0].Items[0].RemoveCommand.ExecuteAsync(null);

        var question = Assert.Single(_services.Dialogs.Confirmations);
        Assert.Contains("Mémoire", question.Message, StringComparison.Ordinal);
        Assert.Contains("n'est pas touché", question.Message, StringComparison.Ordinal);
        Assert.Single(_services.Repository.Stored(workspace.Id)!.Resources);
    }

    [Fact]
    public async Task RemoveResource_ConfirmedRemovesOnlyThatResource()
    {
        var workspace = Seed(
            (ResourceKind.File, "Mémoire", @"C:\a\mémoire.docx"),
            (ResourceKind.Url, "Lien", "https://example.org"));
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = true;

        await page.ResourceGroups[0].Items[0].RemoveCommand.ExecuteAsync(null);

        Assert.Equal(["Lien"], _services.Repository.Stored(workspace.Id)!.Resources.Select(r => r.DisplayName));
        Assert.Equal(["Liens"], page.ResourceGroups.Select(g => g.Title));
    }

    [Fact]
    public async Task RemoveResource_ThatFailsIsReported()
    {
        var workspace = Seed((ResourceKind.File, "Mémoire", @"C:\a\mémoire.docx"));
        var page = await OpenAsync(workspace);
        _services.Dialogs.ConfirmAnswer = true;
        _services.Repository.WriteFailure = new IOException("Disque plein");

        await page.ResourceGroups[0].Items[0].RemoveCommand.ExecuteAsync(null);

        Assert.Contains(TestServices.LogsMarker, page.ActionError, StringComparison.Ordinal);
        Assert.Single(page.ResourceGroups);
    }

    [Fact]
    public async Task Resources_ShowAnInstalledApplicationInWordsAndFlagThoseToApprove()
    {
        var workspace = Workspace.Restore(
            Guid.NewGuid(),
            "Importé",
            string.Empty,
            string.Empty,
            _services.Clock.GetUtcNow(),
            _services.Clock.GetUtcNow(),
            null,
            [Resource(ResourceKind.Application, "Calculatrice", Calculator, requiresReview: true)]);
        _services.Repository.Seed(workspace);

        var item = (await OpenAsync(workspace)).ResourceGroups[0].Items[0];

        Assert.Equal("Application installée sur cet ordinateur", item.Target);
        Assert.True(item.NeedsApproval);
        Assert.Equal("Modifier Calculatrice", item.EditLabel);
        Assert.Equal("Retirer Calculatrice", item.RemoveLabel);
    }

    private Workspace Seed(params (ResourceKind Kind, string Name, string Target)[] resources)
    {
        var workspace = Workspace.Create("Études d'été", null, _services.Clock.GetUtcNow());
        foreach (var (kind, name, target) in resources)
            workspace.AddResource(kind, name, target, _services.Clock.GetUtcNow());

        _services.Repository.Seed(workspace);
        return workspace;
    }

    private Resource Resource(ResourceKind kind, string name, string target, bool requiresReview = false) =>
        new(Guid.NewGuid(), kind, name, target, 0, requiresReview, _services.Clock.GetUtcNow(), _services.Clock.GetUtcNow());

    private async Task<WorkspaceDetailViewModel> OpenAsync(Workspace workspace)
    {
        var page = new WorkspaceDetailViewModel(workspace.Id, _services.Pages, _services.Navigator);
        await page.LoadAsync();
        return page;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 500 && !condition(); i++)
            await Task.Delay(10, TestContext.Current.CancellationToken);

        Assert.True(condition());
    }
}
