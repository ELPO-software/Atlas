using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;

namespace Atlas.Core.Tests;

public sealed class WorkspaceTests
{
    private static readonly DateTimeOffset Created = new(2026, 11, 4, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddHours(3);

    [Fact]
    public void Create_TrimsTheNameAndSetsBothDates()
    {
        var workspace = Workspace.Create("  Développement  ", null, Created);

        Assert.NotEqual(Guid.Empty, workspace.Id);
        Assert.Equal("Développement", workspace.Name);
        Assert.Equal(string.Empty, workspace.Description);
        Assert.Equal(string.Empty, workspace.Notes);
        Assert.Equal(Created, workspace.CreatedAt);
        Assert.Equal(Created, workspace.UpdatedAt);
        Assert.Null(workspace.LastUsedAt);
        Assert.Empty(workspace.Resources);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RefusesAMissingName(string? name)
    {
        var exception = Assert.Throws<AtlasValidationException>(() => Workspace.Create(name, null, Created));

        Assert.Equal(new ValidationError(ValidationField.WorkspaceName, ValidationCode.Required), Assert.Single(exception.Errors));
    }

    [Fact]
    public void Create_RefusesANameThatIsTooLong()
    {
        var name = new string('a', WorkspaceRules.NameMaxLength + 1);

        var exception = Assert.Throws<AtlasValidationException>(() => Workspace.Create(name, null, Created));

        Assert.Equal(ValidationCode.TooLong, Assert.Single(exception.Errors).Code);
    }

    [Fact]
    public void Create_AcceptsANameAtTheLengthLimit()
    {
        var name = new string('a', WorkspaceRules.NameMaxLength);

        Assert.Equal(name, Workspace.Create(name, null, Created).Name);
    }

    [Fact]
    public void Create_RefusesControlCharactersInTheName()
    {
        var exception = Assert.Throws<AtlasValidationException>(() => Workspace.Create("Nom\nsur deux lignes", null, Created));

        Assert.Equal(ValidationCode.ControlCharacters, Assert.Single(exception.Errors).Code);
    }

    [Fact]
    public void Create_RefusesADescriptionThatIsTooLong()
    {
        var description = new string('a', WorkspaceRules.DescriptionMaxLength + 1);

        var exception = Assert.Throws<AtlasValidationException>(() => Workspace.Create("Nom", description, Created));

        Assert.Equal(new ValidationError(ValidationField.WorkspaceDescription, ValidationCode.TooLong), Assert.Single(exception.Errors));
    }

    [Theory]
    [InlineData("Études à l'École — été 2026")]
    [InlineData("Ñandú 日本語 🗺️")]
    public void Create_KeepsAccentsAndOtherScripts(string name) =>
        Assert.Equal(name, Workspace.Create(name, null, Created).Name);

    [Fact]
    public void UpdateDetails_ChangesTheModificationDateOnly()
    {
        var workspace = Workspace.Create("Avant", "Ancienne", Created);

        workspace.UpdateDetails(" Après ", "Nouvelle", Later);

        Assert.Equal("Après", workspace.Name);
        Assert.Equal("Nouvelle", workspace.Description);
        Assert.Equal(Created, workspace.CreatedAt);
        Assert.Equal(Later, workspace.UpdatedAt);
    }

    [Fact]
    public void UpdateDetails_LeavesTheWorkspaceUntouchedWhenRefused()
    {
        var workspace = Workspace.Create("Avant", "Ancienne", Created);

        Assert.Throws<AtlasValidationException>(() => workspace.UpdateDetails(" ", "Nouvelle", Later));

        Assert.Equal("Avant", workspace.Name);
        Assert.Equal("Ancienne", workspace.Description);
        Assert.Equal(Created, workspace.UpdatedAt);
    }

    [Fact]
    public void UpdateNotes_KeepsTheTextExactly()
    {
        const string Notes = "Objectifs :\n\t- finir l'Alpha\r\n\r\n  Tâche à reprendre  ";
        var workspace = Workspace.Create("Nom", null, Created);

        workspace.UpdateNotes(Notes, Later);

        Assert.Equal(Notes, workspace.Notes);
        Assert.Equal(Later, workspace.UpdatedAt);
    }

    [Fact]
    public void UpdateNotes_RefusesTextThatIsTooLong()
    {
        var workspace = Workspace.Create("Nom", null, Created);
        var notes = new string('a', WorkspaceRules.NotesMaxLength + 1);

        Assert.Throws<AtlasValidationException>(() => workspace.UpdateNotes(notes, Later));

        Assert.Equal(string.Empty, workspace.Notes);
        Assert.Equal(Created, workspace.UpdatedAt);
    }

    [Fact]
    public void MarkUsed_DoesNotCountAsAModification()
    {
        var workspace = Workspace.Create("Nom", null, Created);

        workspace.MarkUsed(Later);

        Assert.Equal(Later, workspace.LastUsedAt);
        Assert.Equal(Created, workspace.UpdatedAt);
    }

    [Fact]
    public void AddResource_KeepsPathsWithSpacesAndAccentsExactly()
    {
        const string Path = @"C:\Program Files\Éditeur d'été\Mon Appli.exe";
        var workspace = Workspace.Create("Nom", null, Created);

        var resource = workspace.AddResource(ResourceKind.Application, "Mon Appli", Path, Later);

        Assert.Equal(Path, resource.Target);
        Assert.False(resource.RequiresReview);
        Assert.Equal(Later, workspace.UpdatedAt);
        Assert.Equal(resource, Assert.Single(workspace.Resources));
    }

    [Fact]
    public void AddResource_RefusesAKindThatWasNeverSet()
    {
        var workspace = Workspace.Create("Nom", null, Created);

        Assert.Throws<ArgumentException>(() => workspace.AddResource(default, "Nom", "cible", Later));

        Assert.Empty(workspace.Resources);
    }

    [Theory]
    [InlineData(null, "cible", ValidationField.ResourceDisplayName)]
    [InlineData(" ", "cible", ValidationField.ResourceDisplayName)]
    [InlineData("Nom", null, ValidationField.ResourceTarget)]
    [InlineData("Nom", " ", ValidationField.ResourceTarget)]
    public void AddResource_RefusesMissingValues(string? displayName, string? target, ValidationField field)
    {
        var workspace = Workspace.Create("Nom", null, Created);

        var exception = Assert.Throws<AtlasValidationException>(
            () => workspace.AddResource(ResourceKind.File, displayName, target, Later));

        Assert.Equal(new ValidationError(field, ValidationCode.Required), Assert.Single(exception.Errors));
        Assert.Empty(workspace.Resources);
    }

    [Fact]
    public void AddResource_RefusesLineBreaksInTheTarget()
    {
        var workspace = Workspace.Create("Nom", null, Created);

        var exception = Assert.Throws<AtlasValidationException>(
            () => workspace.AddResource(ResourceKind.File, "Nom", "C:\\a.txt\nC:\\b.exe", Later));

        Assert.Equal(new ValidationError(ValidationField.ResourceTarget, ValidationCode.ControlCharacters), Assert.Single(exception.Errors));
    }

    [Fact]
    public void AddResource_NeverReusesASortOrderAfterARemoval()
    {
        var workspace = Workspace.Create("Nom", null, Created);
        var first = workspace.AddResource(ResourceKind.Url, "A", "a", Later);
        var second = workspace.AddResource(ResourceKind.Url, "B", "b", Later);
        workspace.RemoveResource(first.Id, Later);

        var third = workspace.AddResource(ResourceKind.Url, "C", "c", Later);

        Assert.True(third.SortOrder > second.SortOrder);
    }

    [Fact]
    public void UpdateResource_ChangesOnlyTheNamedResource()
    {
        var workspace = Workspace.Create("Nom", null, Created);
        var kept = workspace.AddResource(ResourceKind.Url, "A", "a", Created);
        var changed = workspace.AddResource(ResourceKind.Url, "B", "b", Created);

        var updated = workspace.UpdateResource(changed.Id, "B2", "b2", Later);

        Assert.Equal("B2", updated.DisplayName);
        Assert.Equal("b2", updated.Target);
        Assert.Equal(changed.Id, updated.Id);
        Assert.Equal(Created, updated.CreatedAt);
        Assert.Equal(Later, updated.UpdatedAt);
        Assert.Equal([kept, updated], workspace.Resources);
    }

    [Fact]
    public void UpdateResource_LeavesTheResourceUntouchedWhenRefused()
    {
        var workspace = Workspace.Create("Nom", null, Created);
        var resource = workspace.AddResource(ResourceKind.Url, "A", "a", Created);

        Assert.Throws<AtlasValidationException>(() => workspace.UpdateResource(resource.Id, "B", " ", Later));

        Assert.Equal(resource, Assert.Single(workspace.Resources));
        Assert.Equal(Created, workspace.UpdatedAt);
    }

    [Fact]
    public void UpdateResource_ThrowsWhenTheResourceDoesNotExist()
    {
        var workspace = Workspace.Create("Nom", null, Created);

        Assert.Throws<ResourceNotFoundException>(() => workspace.UpdateResource(Guid.NewGuid(), "A", "a", Later));
    }

    [Fact]
    public void RemoveResource_ThrowsWhenTheResourceDoesNotExist()
    {
        var workspace = Workspace.Create("Nom", null, Created);

        Assert.Throws<ResourceNotFoundException>(() => workspace.RemoveResource(Guid.NewGuid(), Later));

        Assert.Equal(Created, workspace.UpdatedAt);
    }

    [Fact]
    public void Restore_PutsResourcesInTheirSavedOrder()
    {
        var second = Resource(sortOrder: 2);
        var first = Resource(sortOrder: 1);

        var workspace = Workspace.Restore(Guid.NewGuid(), "Nom", "", "", Created, Created, null, [second, first]);

        Assert.Equal([first, second], workspace.Resources);
    }

    [Fact]
    public void Duplicate_CopiesTheContentWithNewIdentifiersAndDates()
    {
        var source = Workspace.Create("Source", "Description", Created);
        source.UpdateNotes("Notes", Created);
        var resource = source.AddResource(ResourceKind.Folder, "Dossier", @"C:\Données été", Created);
        source.MarkUsed(Created);

        var copy = source.Duplicate(" Copie ", Later);

        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Copie", copy.Name);
        Assert.Equal("Description", copy.Description);
        Assert.Equal("Notes", copy.Notes);
        Assert.Equal(Later, copy.CreatedAt);
        Assert.Equal(Later, copy.UpdatedAt);
        Assert.Null(copy.LastUsedAt);

        var copied = Assert.Single(copy.Resources);
        Assert.NotEqual(resource.Id, copied.Id);
        Assert.Equal(resource.Kind, copied.Kind);
        Assert.Equal(resource.DisplayName, copied.DisplayName);
        Assert.Equal(resource.Target, copied.Target);
        Assert.Equal(Later, copied.CreatedAt);
    }

    [Fact]
    public void Duplicate_KeepsThePendingApproval()
    {
        var source = Workspace.Restore(
            Guid.NewGuid(), "Source", "", "", Created, Created, null, [Resource(sortOrder: 0, requiresReview: true)]);

        var copy = source.Duplicate("Copie", Later);

        Assert.True(Assert.Single(copy.Resources).RequiresReview);
    }

    [Fact]
    public void Duplicate_ProducesAnIndependentWorkspace()
    {
        var source = Workspace.Create("Source", null, Created);
        source.AddResource(ResourceKind.Url, "Lien", "https://example.org", Created);
        var copy = source.Duplicate("Copie", Later);

        copy.UpdateDetails("Copie modifiée", "Autre", Later);
        copy.RemoveResource(copy.Resources[0].Id, Later);

        Assert.Equal("Source", source.Name);
        Assert.Equal(string.Empty, source.Description);
        Assert.Single(source.Resources);
    }

    [Fact]
    public void Duplicate_RefusesAnInvalidName()
    {
        var source = Workspace.Create("Source", null, Created);

        Assert.Throws<AtlasValidationException>(() => source.Duplicate(" ", Later));
    }

    private static Resource Resource(int sortOrder, bool requiresReview = false) =>
        new(Guid.NewGuid(), ResourceKind.File, "Fichier", @"C:\a.txt", sortOrder, requiresReview, Created, Created);
}
