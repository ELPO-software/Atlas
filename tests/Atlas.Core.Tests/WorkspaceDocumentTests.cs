using Atlas.Core.Exchange;
using Atlas.Core.Resources;
using Atlas.Core.Workspaces;

namespace Atlas.Core.Tests;

public sealed class WorkspaceDocumentTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Write_ThenRead_GivesBackEverything()
    {
        var workspace = Workspace.Create("Études d'été", "Mémoire « final »", Now);
        workspace.UpdateNotes("À relire :\r\n- chapitre 3\t(urgent)", Now);
        workspace.AddResource(ResourceKind.File, "Mémoire", @"C:\Données d'été\mémoire.docx", Now);
        workspace.AddResource(ResourceKind.Url, "Bibliothèque", "https://example.org/é?a=1&b=<2>", Now);
        workspace.AddResource(ResourceKind.Parse("carte-mentale"), "Plan", "plan.mm", Now);

        var read = WorkspaceDocument.Read(WorkspaceDocument.Write(workspace, Now));

        Assert.Null(read.Error);
        var content = read.Content!;
        Assert.Equal("Études d'été", content.Name);
        Assert.Equal("Mémoire « final »", content.Description);
        Assert.Equal("À relire :\r\n- chapitre 3\t(urgent)", content.Notes);
        Assert.Equal(
            [
                new DocumentResource("file", "Mémoire", @"C:\Données d'été\mémoire.docx", false),
                new DocumentResource("url", "Bibliothèque", "https://example.org/é?a=1&b=<2>", false),
                new DocumentResource("carte-mentale", "Plan", "plan.mm", false),
            ],
            content.Resources);
    }

    [Fact]
    public void Write_KeepsAccentsReadable()
    {
        var text = WorkspaceDocument.Write(Workspace.Create("Études d'été", null, Now), Now);

        Assert.Contains("\"name\": \"Études d'été\"", text, StringComparison.Ordinal);
        Assert.Contains("\"format\": \"elpo-atlas-workspace\"", text, StringComparison.Ordinal);
        Assert.Contains("\"version\": 1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Read_FillsInWhatIsOptional()
    {
        var read = WorkspaceDocument.Read("""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom" } }""");

        Assert.Equal(new DocumentContent("Nom", string.Empty, string.Empty, []), read.Content! with { Resources = [] });
        Assert.Empty(read.Content!.Resources);
    }

    [Theory]
    [InlineData("pas du JSON", DocumentProblem.InvalidJson)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom", }, }""", DocumentProblem.InvalidJson)]
    [InlineData("""{ /* commentaire */ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom" } }""", DocumentProblem.InvalidJson)]
    [InlineData("""[1, 2, 3]""", DocumentProblem.NotAnAtlasFile)]
    [InlineData("""{ "version": 1, "workspace": { "name": "Nom" } }""", DocumentProblem.NotAnAtlasFile)]
    [InlineData("""{ "format": "autre-chose", "version": 1, "workspace": { "name": "Nom" } }""", DocumentProblem.NotAnAtlasFile)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 2, "workspace": { "name": "Nom" } }""", DocumentProblem.NewerVersion)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 0, "workspace": { "name": "Nom" } }""", DocumentProblem.UnsupportedVersion)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": "1", "workspace": { "name": "Nom" } }""", DocumentProblem.WrongType)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 1.5, "workspace": { "name": "Nom" } }""", DocumentProblem.WrongType)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 1 }""", DocumentProblem.MissingProperty)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { } }""", DocumentProblem.MissingProperty)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": 42 } }""", DocumentProblem.WrongType)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom", "resources": {} } }""", DocumentProblem.WrongType)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom" }, "script": "x" }""", DocumentProblem.UnexpectedProperty)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom", "name": "Autre" } }""", DocumentProblem.DuplicateProperty)]
    [InlineData("""{ "format": "elpo-atlas-workspace", "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom" } }""", DocumentProblem.DuplicateProperty)]
    public void Read_RefusesFilesThatAreNotExactlyTheFormat(string text, DocumentProblem expected)
    {
        var read = WorkspaceDocument.Read(text);

        Assert.Null(read.Content);
        Assert.Equal(expected, read.Error?.Problem);
    }

    [Theory]
    [InlineData("""{ "kind": "application", "name": "Outil", "target": "C:\\outil.exe", "arguments": "--supprimer-tout" }""", "workspace.resources[0].arguments")]
    [InlineData("""{ "kind": "application", "name": "Outil", "target": "C:\\outil.exe", "workingDirectory": "C:\\" }""", "workspace.resources[0].workingDirectory")]
    public void Read_RefusesArgumentsOrAnythingElseToRunWith(string resource, string location)
    {
        var read = WorkspaceDocument.Read(Document(resource));

        Assert.Equal(new DocumentError(DocumentProblem.UnexpectedProperty, location), read.Error);
    }

    [Theory]
    [InlineData("""{ "kind": "url", "name": "Lien", "target": 42 }""", "workspace.resources[0].target")]
    [InlineData("""{ "kind": "url", "name": "Lien", "target": "https://example.org", "requiresReview": "non" }""", "workspace.resources[0].requiresReview")]
    [InlineData("""{ "kind": "url", "name": "Lien" }""", "workspace.resources[0].target")]
    [InlineData("""{ "kind": "url", "name": "Lien", "target": "https://example.org", "target": "file:///C:/x.exe" }""", "workspace.resources[0].target")]
    public void Read_SaysWhereAResourceIsMalformed(string resource, string location)
    {
        var read = WorkspaceDocument.Read(Document(resource));

        Assert.Equal(location, read.Error?.Location);
    }

    [Fact]
    public void Read_RefusesMoreThanTheResourceLimit()
    {
        var resource = """{ "kind": "url", "name": "Lien", "target": "https://example.org" }""";
        var text = Document(string.Join(",", Enumerable.Repeat(resource, WorkspaceDocument.MaxResources + 1)));

        Assert.Equal(DocumentProblem.TooManyResources, WorkspaceDocument.Read(text).Error?.Problem);
    }

    [Fact]
    public void Read_AcceptsExactlyTheResourceLimit()
    {
        var resource = """{ "kind": "url", "name": "Lien", "target": "https://example.org" }""";
        var text = Document(string.Join(",", Enumerable.Repeat(resource, WorkspaceDocument.MaxResources)));

        Assert.Equal(WorkspaceDocument.MaxResources, WorkspaceDocument.Read(text).Content?.Resources.Count);
    }

    [Fact]
    public void Read_RefusesDeepNestingWithoutGoingFurther()
    {
        var nested = new string('[', 64) + new string(']', 64);
        var text = $$"""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom", "notes": {{nested}} } }""";

        Assert.Equal(DocumentProblem.InvalidJson, WorkspaceDocument.Read(text).Error?.Problem);
    }

    [Fact]
    public void Read_RefusesTextLargerThanTheLimit()
    {
        var notes = new string('é', (WorkspaceDocument.MaxBytes / 2) + 1);
        var text = $$"""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom", "notes": "{{notes}}" } }""";

        Assert.Equal(DocumentProblem.TooLarge, WorkspaceDocument.Read(text).Error?.Problem);
    }

    [Fact]
    public void Read_SaysOnWhichLineTheJsonBreaks()
    {
        var read = WorkspaceDocument.Read("{\n  \"format\": \"elpo-atlas-workspace\",\n  \"version\": 1,\n  oups\n}");

        Assert.Equal(new DocumentError(DocumentProblem.InvalidJson, "4"), read.Error);
    }

    private static string Document(string resources) =>
        $$"""{ "format": "elpo-atlas-workspace", "version": 1, "workspace": { "name": "Nom", "resources": [ {{resources}} ] } }""";
}
