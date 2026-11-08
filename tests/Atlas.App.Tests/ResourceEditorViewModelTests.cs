using Atlas.App.Services;
using Atlas.App.ViewModels;
using Atlas.Core.Resources;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.App.Tests;

public sealed class ResourceEditorViewModelTests
{
    private const string Calculator = @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App";

    private readonly TestServices _services = new();
    private readonly Guid _workspaceId;

    public ResourceEditorViewModelTests() => _workspaceId = _services.SeedWorkspace().Id;

    [Fact]
    public void NewResource_StartsAsAnApplicationAndLetsTheUserChooseTheType()
    {
        var form = AddForm();

        Assert.True(form.CanChooseKind);
        Assert.True(form.IsApplication);

        form.IsLink = true;

        Assert.Equal(ResourceKind.Url, form.Kind);
        Assert.False(form.IsApplication);
        Assert.Equal("Adresse web", form.TargetLabel);
        Assert.False(form.CanBrowse);
    }

    [Fact]
    public void ExistingResource_KeepsItsType()
    {
        var form = EditForm(ResourceKind.File, "Mémoire", @"C:\a\mémoire.docx");

        form.IsFolder = true;

        Assert.False(form.CanChooseKind);
        Assert.Equal(ResourceKind.File, form.Kind);
        Assert.Equal("Emplacement du fichier", form.TargetLabel);
        Assert.Equal("Mémoire", form.DisplayName);
        Assert.Equal(@"C:\a\mémoire.docx", form.Target);
    }

    [Theory]
    [InlineData(TestKind.Application, "program")]
    [InlineData(TestKind.File, "file")]
    [InlineData(TestKind.Folder, "folder")]
    public async Task Browse_UsesThePickerThatFitsTheType(TestKind kind, string expected)
    {
        var form = AddForm();
        Choose(form, kind);
        _services.Dialogs.Picker.NextPath = @"C:\Données d'été\choix";

        await form.BrowseCommand.ExecuteAsync(null);

        Assert.Equal([expected], _services.Dialogs.Picker.Requests);
        Assert.Equal(@"C:\Données d'été\choix", form.Target);
    }

    [Fact]
    public async Task Browse_CancelledKeepsWhatWasThere()
    {
        var form = EditForm(ResourceKind.File, "Mémoire", @"C:\a\mémoire.docx");

        await form.BrowseCommand.ExecuteAsync(null);

        Assert.Equal(@"C:\a\mémoire.docx", form.Target);
    }

    [Theory]
    [InlineData(TestKind.Application, @"C:\Programmes\Éditeur d'été\Éditeur.exe", "Éditeur")]
    [InlineData(TestKind.File, @"C:\Données d'été\mémoire final.docx", "mémoire final.docx")]
    [InlineData(TestKind.Folder, @"C:\Données d'été\Sources\", "Sources")]
    [InlineData(TestKind.Link, "https://bibliotheque.example.org/catalogue", "bibliotheque.example.org")]
    public void Name_IsSuggestedFromTheTarget(TestKind kind, string target, string expected)
    {
        var form = AddForm();
        Choose(form, kind);

        form.Target = target;

        Assert.Equal(expected, form.DisplayName);
    }

    [Fact]
    public void Name_FollowsTheTargetUntilTheUserTypesOne()
    {
        var form = AddForm();
        form.IsFile = true;
        form.Target = @"C:\a\premier.txt";
        form.Target = @"C:\a\second.txt";

        Assert.Equal("second.txt", form.DisplayName);

        form.DisplayName = "Mon nom";
        form.Target = @"C:\a\troisième.txt";

        Assert.Equal("Mon nom", form.DisplayName);
    }

    [Fact]
    public async Task InstalledApplications_AreListedAndFilteredWithoutCaringAboutAccents()
    {
        _services.Dialogs.Applications.Installed.AddRange(
        [
            new InstalledApplication("Calculatrice", Calculator),
            new InstalledApplication("Éditeur de texte", @"shell:AppsFolder\Editeur"),
            new InstalledApplication("Musique", @"shell:AppsFolder\Musique"),
        ]);
        var form = AddForm();

        await form.ChooseInstalledApplicationCommand.ExecuteAsync(null);
        Assert.True(form.IsChoosingApplication);
        Assert.Equal(3, form.MatchingApplications.Count);

        form.SearchText = "editeur";

        Assert.Equal(["Éditeur de texte"], form.MatchingApplications.Select(a => a.Name));
    }

    [Fact]
    public async Task InstalledApplications_AreReadOnlyOnce()
    {
        var form = AddForm();

        await form.ChooseInstalledApplicationCommand.ExecuteAsync(null);
        await form.ChooseInstalledApplicationCommand.ExecuteAsync(null);

        Assert.Equal(1, _services.Dialogs.Applications.Reads);
    }

    [Fact]
    public async Task InstalledApplication_ChosenFillsTheFormAndClosesTheList()
    {
        _services.Dialogs.Applications.Installed.Add(new InstalledApplication("Calculatrice", Calculator));
        var form = AddForm();
        await form.ChooseInstalledApplicationCommand.ExecuteAsync(null);

        form.SelectedApplication = form.MatchingApplications[0];

        Assert.Equal(Calculator, form.Target);
        Assert.Equal("Calculatrice", form.DisplayName);
        Assert.False(form.IsChoosingApplication);
    }

    [Fact]
    public async Task InstalledApplications_ThatCannotBeReadAreExplainedWithAWayOut()
    {
        _services.Dialogs.Applications.Failure = new InvalidOperationException("Shell indisponible");
        var form = AddForm();

        await form.ChooseInstalledApplicationCommand.ExecuteAsync(null);

        Assert.Contains("Parcourir", form.ApplicationsError, StringComparison.Ordinal);
        Assert.Contains(TestServices.LogsMarker, form.ApplicationsError, StringComparison.Ordinal);
        Assert.False(form.HasNoMatchingApplication);
    }

    [Fact]
    public async Task InstalledApplications_SayWhenNothingMatches()
    {
        _services.Dialogs.Applications.Installed.Add(new InstalledApplication("Calculatrice", Calculator));
        var form = AddForm();
        await form.ChooseInstalledApplicationCommand.ExecuteAsync(null);

        form.SearchText = "introuvable";

        Assert.True(form.HasNoMatchingApplication);
    }

    [Fact]
    public async Task Submit_AddsTheResourceAndAsksToClose()
    {
        var form = AddForm();
        var completions = 0;
        form.Completed += (_, _) => completions++;
        form.IsLink = true;
        form.Target = "https://example.org/été";

        await form.SubmitCommand.ExecuteAsync(null);

        var resource = Assert.Single(form.Result!.Resources);
        Assert.Equal(ResourceKind.Url, resource.Kind);
        Assert.Equal("example.org", resource.DisplayName);
        Assert.Equal(1, completions);
    }

    [Theory]
    [InlineData(TestKind.Link, "example.org", "https://")]
    [InlineData(TestKind.File, @"C:\a\installer.exe", "exécuter du code")]
    [InlineData(TestKind.File, "notes.txt", "chemin complet")]
    [InlineData(TestKind.Application, @"C:\a\lancer.bat", "programme (.exe)")]
    public async Task Submit_ExplainsWhyATargetIsRefusedAndStaysOpen(TestKind kind, string target, string expected)
    {
        var form = AddForm();
        Choose(form, kind);
        form.Target = target;
        form.DisplayName = "Nom";

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.Contains(expected, form.TargetError, StringComparison.Ordinal);
        Assert.Null(form.Result);
        Assert.Empty(_services.Repository.Stored(_workspaceId)!.Resources);
    }

    [Fact]
    public async Task Submit_AsksForANameWhenNoneCouldBeSuggested()
    {
        var form = AddForm();
        form.IsFolder = true;
        form.Target = @"C:\";

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.Equal("Donnez un nom à cette ressource.", form.DisplayNameError);
        Assert.Null(form.TargetError);
    }

    [Fact]
    public async Task Submit_ExplainsAFailedSaveAndKeepsWhatWasTyped()
    {
        _services.Repository.WriteFailure = new IOException("Disque plein");
        var form = AddForm();
        form.IsLink = true;
        form.Target = "https://example.org";

        await form.SubmitCommand.ExecuteAsync(null);

        Assert.Contains(TestServices.LogsMarker, form.SaveError, StringComparison.Ordinal);
        Assert.Equal("https://example.org", form.Target);
    }

    [Fact]
    public void ChangingTheType_ClearsTheTargetError()
    {
        var form = AddForm();
        form.IsLink = true;
        form.Target = "pas une adresse";
        form.SubmitCommand.Execute(null);
        Assert.NotNull(form.TargetError);

        form.IsFile = true;

        Assert.Null(form.TargetError);
    }

    public enum TestKind
    {
        Application,
        File,
        Folder,
        Link,
    }

    private static void Choose(ResourceEditorViewModel form, TestKind kind)
    {
        switch (kind)
        {
            case TestKind.Application:
                form.IsApplication = true;
                break;
            case TestKind.File:
                form.IsFile = true;
                break;
            case TestKind.Folder:
                form.IsFolder = true;
                break;
            default:
                form.IsLink = true;
                break;
        }
    }

    private ResourceEditorViewModel AddForm() =>
        Form(new ResourceEditorRequest(
            "Ajouter",
            "Ajouter",
            FixedKind: null,
            string.Empty,
            string.Empty,
            (kind, name, target) => _services.Workspaces.AddResourceAsync(_workspaceId, kind, name, target)));

    private ResourceEditorViewModel EditForm(ResourceKind kind, string name, string target) =>
        Form(new ResourceEditorRequest(
            "Modifier",
            "Enregistrer",
            kind,
            name,
            target,
            (_, n, t) => throw new InvalidOperationException("Not saved in this test.")));

    private ResourceEditorViewModel Form(ResourceEditorRequest request) =>
        new(request, _services.Dialogs.Picker, _services.Dialogs.Applications, _services.Paths, NullLogger<ResourceEditorViewModel>.Instance);
}
