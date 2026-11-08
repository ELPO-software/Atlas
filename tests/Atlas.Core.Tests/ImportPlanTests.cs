using Atlas.Core.Exchange;
using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;

namespace Atlas.Core.Tests;

public sealed class ImportPlanTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("application", @"C:\Programmes\Éditeur.exe", ImportNote.NeedsApproval)]
    [InlineData("application", @"shell:AppsFolder\Microsoft.WindowsCalculator_8wekyb3d8bbwe!App", ImportNote.NeedsApproval)]
    [InlineData("file", @"\\serveur\partage\notes.txt", ImportNote.NeedsApproval)]
    [InlineData("folder", "//serveur/partage", ImportNote.NeedsApproval)]
    [InlineData("file", @"C:\Données d'été\notes.txt", ImportNote.None)]
    [InlineData("folder", @"C:\Données d'été", ImportNote.None)]
    [InlineData("url", "https://example.org", ImportNote.None)]
    [InlineData("carte-mentale", "plan.mm", ImportNote.UnknownKind)]
    public void Resources_AreClassifiedFromTheFileAlone(string kind, string target, ImportNote expected) =>
        Assert.Equal(expected, Assert.Single(Plan(Resource(kind, target)).Resources).Note);

    [Fact]
    public void Resources_NeverInheritTheApprovalWrittenInTheFile()
    {
        var plan = Plan(new DocumentResource("application", "Outil", @"C:\outil.exe", RequiresReview: false));

        Assert.Equal(ImportNote.NeedsApproval, Assert.Single(plan.Resources).Note);
    }

    [Theory]
    [InlineData("file", @"C:\a\lancer.bat", ValidationCode.ExecutableFile)]
    [InlineData("file", @"C:\a\raccourci.lnk", ValidationCode.ExecutableFile)]
    [InlineData("file", @"C:\notes.txt:caché.exe", ValidationCode.InvalidPath)]
    [InlineData("file", @"%APPDATA%\notes.txt", ValidationCode.InvalidPath)]
    [InlineData("folder", @"..\..\Windows", ValidationCode.InvalidPath)]
    [InlineData("url", "javascript:alert(1)", ValidationCode.UnsupportedLink)]
    [InlineData("url", "file:///C:/Windows/System32/cmd.exe", ValidationCode.UnsupportedLink)]
    [InlineData("application", @"C:\a\script.ps1", ValidationCode.NotAnApplication)]
    [InlineData("application", "cmd.exe /c del", ValidationCode.NotAnApplication)]
    public void Resources_ThatBreakARuleAreRefusedWithTheRule(string kind, string target, ValidationCode expected)
    {
        var resource = Assert.Single(Plan(Resource(kind, target)).Resources);

        Assert.Equal(ImportNote.Refused, resource.Note);
        Assert.Equal(expected, resource.Refusal?.Code);
    }

    [Fact]
    public void Resources_WithAnEmptyNameAreRefused()
    {
        var resource = Assert.Single(Plan(new DocumentResource("url", " ", "https://example.org", false)).Resources);

        Assert.Equal(new ValidationError(ValidationField.ResourceDisplayName, ValidationCode.Required), resource.Refusal);
    }

    [Theory]
    [InlineData("Application")]
    [InlineData("url!")]
    [InlineData("")]
    public void Resources_WithAnUnreadableTypeAreRefused(string kind)
    {
        var resource = Assert.Single(Plan(new DocumentResource(kind, "Nom", "https://example.org", false)).Resources);

        Assert.Equal(ImportNote.Refused, resource.Note);
        Assert.Null(resource.Refusal);
    }

    [Fact]
    public void Workspace_WithAnInvalidNameCannotBeImported()
    {
        var plan = ImportPlan.From(new DocumentContent(" ", string.Empty, string.Empty, []));

        Assert.False(plan.CanImport);
        Assert.Equal(new ValidationError(ValidationField.WorkspaceName, ValidationCode.Required), Assert.Single(plan.WorkspaceErrors));
        Assert.Throws<AtlasValidationException>(() => Workspace.Import(plan, Now));
    }

    [Fact]
    public void Workspace_WithNotesTooLongCannotBeImported()
    {
        var plan = ImportPlan.From(new DocumentContent("Nom", string.Empty, new string('é', WorkspaceRules.NotesMaxLength + 1), []));

        Assert.False(plan.CanImport);
    }

    [Fact]
    public void Import_CreatesANewWorkspaceWithoutWhatWasRefused()
    {
        var plan = ImportPlan.From(new DocumentContent(
            "  Études d'été  ",
            "Mémoire",
            "Notes",
            [
                Resource("url", "https://example.org"),
                Resource("file", @"C:\a\lancer.bat"),
                Resource("application", @"C:\Programmes\Éditeur.exe"),
                Resource("carte-mentale", "plan.mm"),
            ]));

        var workspace = Workspace.Import(plan, Now);

        Assert.Equal("Études d'été", workspace.Name);
        Assert.Equal(("Mémoire", "Notes"), (workspace.Description, workspace.Notes));
        Assert.Equal((Now, Now, (DateTimeOffset?)null), (workspace.CreatedAt, workspace.UpdatedAt, workspace.LastUsedAt));
        Assert.Equal(["url", "application", "carte-mentale"], workspace.Resources.Select(r => r.Kind.Value));
        Assert.Equal([false, true, true], workspace.Resources.Select(r => r.RequiresReview));
        Assert.Equal([0, 1, 2], workspace.Resources.Select(r => r.SortOrder));
    }

    [Fact]
    public void Import_GivesNewIdentifiersEveryTime()
    {
        var plan = Plan(Resource("url", "https://example.org"));

        var first = Workspace.Import(plan, Now);
        var second = Workspace.Import(plan, Now);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.Resources[0].Id, second.Resources[0].Id);
    }

    private static ImportPlan Plan(params DocumentResource[] resources) =>
        ImportPlan.From(new DocumentContent("Espace", string.Empty, string.Empty, resources));

    private static DocumentResource Resource(string kind, string target) => new(kind, "Nom", target, false);
}
