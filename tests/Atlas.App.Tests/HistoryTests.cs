using Atlas.App.Text;
using Atlas.App.ViewModels;
using Atlas.Core.Exchange;
using Atlas.Core.History;

namespace Atlas.App.Tests;

public sealed class HistoryTests
{
    private const string Space = "\u00A0";

    private readonly TestServices _services = new();

    [Fact]
    public async Task Page_ListsTheMostRecentFirstWithReadableDates()
    {
        var id = Guid.NewGuid();
        _services.Repository.History.Add(new HistoryEntry(_services.Clock.GetUtcNow().AddDays(-3), HistoryAction.WorkspaceCreated, id, "Études"));
        _services.Repository.History.Add(new HistoryEntry(_services.Clock.GetUtcNow().AddDays(-1), HistoryAction.WorkspaceDeleted, id, "Études"));

        var page = new HistoryPageViewModel(_services.Pages);
        await page.LoadAsync();

        Assert.Equal(
            [($"Espace «{Space}Études{Space}» supprimé", "Hier à 12:00"), ($"Espace «{Space}Études{Space}» créé", "3 novembre 2026 à 12:00")],
            page.Items.Select(i => (i.Text, i.When)));
        Assert.False(page.IsEmpty);
    }

    [Fact]
    public async Task Page_SaysWhenThereIsNothingYet()
    {
        var page = new HistoryPageViewModel(_services.Pages);

        Assert.False(page.IsEmpty);
        await page.LoadAsync();

        Assert.True(page.IsEmpty);
    }

    [Fact]
    public async Task Page_ExplainsWhenTheHistoryCannotBeRead()
    {
        _services.History.Failure = new IOException("Disque indisponible");
        var page = new HistoryPageViewModel(_services.Pages);

        await page.LoadAsync();

        Assert.Contains(TestServices.LogsMarker, page.LoadError, StringComparison.Ordinal);
        Assert.False(page.IsEmpty);
    }

    [Theory]
    [InlineData(HistoryAction.WorkspaceCreated, "", "Espace «\u00A0Études\u00A0» créé")]
    [InlineData(HistoryAction.WorkspaceDuplicated, "Source", "Espace «\u00A0Études\u00A0» créé en dupliquant «\u00A0Source\u00A0»")]
    [InlineData(HistoryAction.WorkspaceImported, "partagé.json", "Espace «\u00A0Études\u00A0» importé depuis partagé.json")]
    [InlineData(HistoryAction.WorkspaceEdited, "", "Espace «\u00A0Études\u00A0» modifié")]
    [InlineData(HistoryAction.NotesEdited, "", "Notes de «\u00A0Études\u00A0» modifiées")]
    [InlineData(HistoryAction.ResourceAdded, "Lien", "Ressource «\u00A0Lien\u00A0» ajoutée à «\u00A0Études\u00A0»")]
    [InlineData(HistoryAction.ResourceEdited, "Lien", "Ressource «\u00A0Lien\u00A0» de «\u00A0Études\u00A0» modifiée")]
    [InlineData(HistoryAction.ResourceRemoved, "Lien", "Ressource «\u00A0Lien\u00A0» retirée de «\u00A0Études\u00A0»")]
    [InlineData(HistoryAction.ResourceApproved, "Outil", "Ressource «\u00A0Outil\u00A0» de «\u00A0Études\u00A0» approuvée")]
    [InlineData(HistoryAction.WorkspaceDeleted, "", "Espace «\u00A0Études\u00A0» supprimé")]
    [InlineData(HistoryAction.VersionSaved, "Propre", "Version «\u00A0Propre\u00A0» de «\u00A0Études\u00A0» enregistrée")]
    [InlineData(HistoryAction.VersionRestored, "Propre", "«\u00A0Études\u00A0» rétabli à la version «\u00A0Propre\u00A0»")]
    [InlineData(HistoryAction.WorkspaceExported, "études.json", "«\u00A0Études\u00A0» exporté vers études.json")]
    public void Text_DescribesEveryAction(HistoryAction action, string detail, string expected) =>
        Assert.Equal(expected, HistoryText.Describe(new HistoryEntry(DateTimeOffset.UnixEpoch, action, Guid.NewGuid(), "Études", detail)));

    [Fact]
    public void Text_SummarizesARestorationAndFlagsProblems()
    {
        var entry = new HistoryEntry(DateTimeOffset.UnixEpoch, HistoryAction.WorkspaceRestored, Guid.NewGuid(), "Études", Counts: new RestoreCounts(3, 1, 0, 2));

        Assert.Equal($"«{Space}Études{Space}» restauré{Space}: 3 restaurées, 1 introuvable, 2 ignorées", HistoryText.Describe(entry));
        Assert.True(HistoryText.IsProblem(entry));
        Assert.False(HistoryText.IsProblem(entry with { Counts = new RestoreCounts(3, 0, 0, 2) }));
    }

    [Theory]
    [InlineData(@"C:\Users\jackp\Documents\notes.txt", "jackp")]
    [InlineData(@"c:\users\JACKP\Desktop", "jackp")]
    [InlineData(@"\\serveur\partage\Users\jackp\notes.txt", "jackp")]
    [InlineData(@"C:\Données\notes.txt", null)]
    [InlineData(@"C:\Users\jackpons\notes.txt", null)]
    [InlineData("https://example.org/jackp", null)]
    public void Privacy_FindsTheUserNameInLocations(string target, string? expected) =>
        Assert.Equal(expected, ExportPrivacy.RevealedUserName([target], "jackp", @"C:\Users\jackp"));

    [Fact]
    public void Privacy_FindsAProfileFolderMovedOutOfUsers()
    {
        Assert.Equal("jp", ExportPrivacy.RevealedUserName([@"D:\Profils\jp\Documents\notes.txt"], "jp", @"D:\Profils\jp"));
        Assert.Equal("jp", ExportPrivacy.RevealedUserName([@"D:\Profils\jp"], "jp", @"D:\Profils\jp\"));
        Assert.Null(ExportPrivacy.RevealedUserName([@"D:\Profils\jpx\notes.txt"], "jp", @"D:\Profils\jp"));
    }

    [Theory]
    [InlineData("Études d'été", "Études d'été.json")]
    [InlineData("Projet: v2/final?", "Projet- v2-final-.json")]
    [InlineData("...", "atlas.json")]
    public void Privacy_SuggestsAFileNameWindowsAccepts(string name, string expected) =>
        Assert.Equal(expected, ExportPrivacy.FileNameFor(name));

    [Theory]
    [InlineData(DocumentProblem.TooLarge, "", "2")]
    [InlineData(DocumentProblem.DuplicateProperty, "workspace.name", "workspace.name")]
    [InlineData(DocumentProblem.UnexpectedProperty, "workspace.resources[0].arguments", "arguments")]
    [InlineData(DocumentProblem.NewerVersion, "", "plus récente")]
    [InlineData(DocumentProblem.TooManyResources, "", "1\u00A0000")]
    public void Document_ProblemsAreExplained(DocumentProblem problem, string location, string expected) =>
        Assert.Contains(expected, DocumentText.Describe(new DocumentError(problem, location)), StringComparison.Ordinal);
}
