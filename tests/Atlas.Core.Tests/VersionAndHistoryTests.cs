using Atlas.Core.Exchange;
using Atlas.Core.History;
using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Versions;
using Atlas.Core.Workspaces;
using Microsoft.Extensions.Time.Testing;

namespace Atlas.Core.Tests;

public sealed class VersionAndHistoryTests
{
    private readonly InMemoryWorkspaceRepository _repository = new();
    private readonly InMemoryVersionRepository _versions;
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 8, 10, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceService _workspaces;
    private readonly VersionService _service;

    public VersionAndHistoryTests()
    {
        _versions = new InMemoryVersionRepository(_repository);
        _workspaces = new WorkspaceService(_repository, _clock);
        _service = new VersionService(_workspaces, _versions, _clock);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EveryChange_IsRecordedWithTheNameAtTheTime()
    {
        var workspace = await _workspaces.CreateAsync("Études", null, Token);
        await _workspaces.UpdateDetailsAsync(workspace.Id, "Études d'été", null, Token);
        await _workspaces.UpdateNotesAsync(workspace.Id, "Notes", Token);
        var withResource = await _workspaces.AddResourceAsync(workspace.Id, ResourceKind.Url, "Lien", "https://example.org", Token);
        var resourceId = withResource.Resources[0].Id;
        await _workspaces.UpdateResourceAsync(workspace.Id, resourceId, "Bibliothèque", "https://example.org", Token);
        await _workspaces.RemoveResourceAsync(workspace.Id, resourceId, Token);
        await _workspaces.MarkUsedAsync(workspace.Id, new RestoreCounts(2, 1, 0, 1), Token);
        await _workspaces.DuplicateAsync(workspace.Id, "Copie", Token);
        await _workspaces.DeleteAsync(workspace.Id, Token);

        Assert.Equal(
            [
                (HistoryAction.WorkspaceCreated, "Études", ""),
                (HistoryAction.WorkspaceEdited, "Études d'été", ""),
                (HistoryAction.NotesEdited, "Études d'été", ""),
                (HistoryAction.ResourceAdded, "Études d'été", "Lien"),
                (HistoryAction.ResourceEdited, "Études d'été", "Bibliothèque"),
                (HistoryAction.ResourceRemoved, "Études d'été", "Bibliothèque"),
                (HistoryAction.WorkspaceRestored, "Études d'été", ""),
                (HistoryAction.WorkspaceDuplicated, "Copie", "Études d'été"),
                (HistoryAction.WorkspaceDeleted, "Études d'été", ""),
            ],
            _repository.History.Select(e => (e.Action, e.WorkspaceName, e.Detail)));
        Assert.Equal(new RestoreCounts(2, 1, 0, 1), _repository.History[6].Counts);
        Assert.All(_repository.History, e => Assert.Equal(_clock.GetUtcNow(), e.OccurredAt));
    }

    [Fact]
    public async Task RefusedChanges_AreNotRecorded()
    {
        var workspace = await _workspaces.CreateAsync("Études", null, Token);

        await Assert.ThrowsAsync<AtlasValidationException>(() => _workspaces.UpdateDetailsAsync(workspace.Id, " ", null, Token));
        await Assert.ThrowsAsync<AtlasValidationException>(() =>
            _workspaces.AddResourceAsync(workspace.Id, ResourceKind.File, "Script", @"C:\a\lancer.bat", Token));

        Assert.Equal([HistoryAction.WorkspaceCreated], _repository.History.Select(e => e.Action));
    }

    [Fact]
    public async Task Import_IsRecordedWithTheFileName()
    {
        var plan = ImportPlan.From(new DocumentContent("Importé", string.Empty, string.Empty, []));

        var workspace = await _workspaces.ImportAsync(plan, "études.json", Token);

        var entry = Assert.Single(_repository.History);
        Assert.Equal((HistoryAction.WorkspaceImported, workspace.Id, "études.json"), (entry.Action, entry.WorkspaceId, entry.Detail));
    }

    [Fact]
    public async Task ApproveResource_LetsAtlasOpenItAndIsRecorded()
    {
        var plan = ImportPlan.From(new DocumentContent("Importé", string.Empty, string.Empty, [new DocumentResource("application", "Outil", @"C:\outil.exe", false)]));
        var workspace = await _workspaces.ImportAsync(plan, "fichier.json", Token);

        var approved = await _workspaces.ApproveResourceAsync(workspace.Id, workspace.Resources[0].Id, Token);

        Assert.False(approved.Resources[0].RequiresReview);
        Assert.False((await _workspaces.GetAsync(workspace.Id, Token)).Resources[0].RequiresReview);
        Assert.Equal((HistoryAction.ResourceApproved, "Outil"), (_repository.History[^1].Action, _repository.History[^1].Detail));
    }

    [Fact]
    public async Task SaveVersion_KeepsTheWholeStateAndIsRecorded()
    {
        var workspace = await SeedAsync();

        var summary = await _service.SaveAsync(workspace.Id, "  Avant les révisions  ", Token);

        Assert.Equal(("Avant les révisions", false, 1), (summary.Name, summary.IsAutomatic, summary.ResourceCount));
        var stored = Assert.Single(_versions.Versions);
        var content = WorkspaceDocument.Read(stored.Content).Content!;
        Assert.Equal(("Études", "Mémoire", "Notes de départ"), (content.Name, content.Description, content.Notes));
        Assert.Equal((HistoryAction.VersionSaved, "Avant les révisions"), (_repository.History[^1].Action, _repository.History[^1].Detail));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task SaveVersion_WithoutANameSavesNothing(string name)
    {
        var workspace = await SeedAsync();

        var exception = await Assert.ThrowsAsync<AtlasValidationException>(() => _service.SaveAsync(workspace.Id, name, Token));

        Assert.Equal(ValidationField.VersionName, Assert.Single(exception.Errors).Field);
        Assert.Empty(_versions.Versions);
    }

    [Fact]
    public async Task RestoreVersion_SavesTheCurrentStateFirstThenGoesBack()
    {
        var workspace = await SeedAsync();
        var version = await _service.SaveAsync(workspace.Id, "Avant", Token);
        _clock.Advance(TimeSpan.FromHours(1));
        await _workspaces.UpdateDetailsAsync(workspace.Id, "Études modifiées", "Autre", Token);
        await _workspaces.UpdateNotesAsync(workspace.Id, "Notes plus récentes", Token);
        await _workspaces.AddResourceAsync(workspace.Id, ResourceKind.Folder, "Sources", @"C:\Sources", Token);

        var restored = await _service.RestoreAsync(version.Id, "Avant le retour", Token);

        Assert.Equal(("Études", "Mémoire", "Notes de départ"), (restored.Name, restored.Description, restored.Notes));
        Assert.Equal(["Lien"], restored.Resources.Select(r => r.DisplayName));
        Assert.Equal(_clock.GetUtcNow(), restored.UpdatedAt);

        var safety = _versions.Versions.Single(v => v.IsAutomatic);
        Assert.Equal("Avant le retour", safety.Name);
        var kept = WorkspaceDocument.Read(safety.Content).Content!;
        Assert.Equal(("Études modifiées", "Notes plus récentes", 2), (kept.Name, kept.Notes, kept.Resources.Count));

        Assert.Equal(
            [(HistoryAction.VersionSaved, "Avant le retour"), (HistoryAction.VersionRestored, "Avant")],
            _repository.History.TakeLast(2).Select(e => (e.Action, e.Detail)));
    }

    [Fact]
    public async Task RestoreVersion_KeepsTheApprovalStateItHad()
    {
        var plan = ImportPlan.From(new DocumentContent("Importé", string.Empty, string.Empty, [new DocumentResource("application", "Outil", @"C:\outil.exe", false)]));
        var workspace = await _workspaces.ImportAsync(plan, "fichier.json", Token);
        var version = await _service.SaveAsync(workspace.Id, "À approuver", Token);
        await _workspaces.ApproveResourceAsync(workspace.Id, workspace.Resources[0].Id, Token);

        var restored = await _service.RestoreAsync(version.Id, "Avant le retour", Token);

        Assert.True(restored.Resources[0].RequiresReview);
    }

    [Fact]
    public async Task RestoreVersion_ThatDoesNotExistChangesNothing()
    {
        var workspace = await SeedAsync();

        await Assert.ThrowsAsync<VersionNotFoundException>(() => _service.RestoreAsync(Guid.NewGuid(), "Avant", Token));

        Assert.Empty(_versions.Versions);
        Assert.Equal("Études", (await _workspaces.GetAsync(workspace.Id, Token)).Name);
    }

    [Fact]
    public async Task RestoreVersion_ThatCannotBeReadChangesNothing()
    {
        var workspace = await SeedAsync();
        var broken = new WorkspaceVersion(Guid.NewGuid(), workspace.Id, "Abîmée", _clock.GetUtcNow(), false, 0, "{ pas du JSON");
        _versions.Versions.Add(broken);

        await Assert.ThrowsAsync<UnreadableVersionException>(() => _service.RestoreAsync(broken.Id, "Avant", Token));

        Assert.Single(_versions.Versions);
        Assert.Equal("Études", (await _workspaces.GetAsync(workspace.Id, Token)).Name);
    }

    [Fact]
    public async Task Versions_AreListedMostRecentFirstAndCounted()
    {
        var workspace = await SeedAsync();
        await _service.SaveAsync(workspace.Id, "Première", Token);
        _clock.Advance(TimeSpan.FromMinutes(1));
        await _service.SaveAsync(workspace.Id, "Seconde", Token);

        Assert.Equal(["Seconde", "Première"], (await _service.ListAsync(workspace.Id, Token)).Select(v => v.Name));
        Assert.Equal(2, await _service.CountAsync(workspace.Id, Token));
    }

    private async Task<Workspace> SeedAsync()
    {
        var workspace = await _workspaces.CreateAsync("Études", "Mémoire", Token);
        await _workspaces.UpdateNotesAsync(workspace.Id, "Notes de départ", Token);
        return await _workspaces.AddResourceAsync(workspace.Id, ResourceKind.Url, "Lien", "https://example.org", Token);
    }

    private sealed class InMemoryVersionRepository(InMemoryWorkspaceRepository workspaces) : IVersionRepository
    {
        public List<WorkspaceVersion> Versions { get; } = [];

        public Task<IReadOnlyList<VersionSummary>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<VersionSummary>>(Versions
                .Where(v => v.WorkspaceId == workspaceId)
                .OrderByDescending(v => v.SavedAt)
                .Select(v => new VersionSummary(v.Id, v.WorkspaceId, v.Name, v.SavedAt, v.IsAutomatic, v.ResourceCount))
                .ToList());

        public Task<int> CountAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Versions.Count(v => v.WorkspaceId == workspaceId));

        public Task<WorkspaceVersion?> GetAsync(Guid versionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Versions.FirstOrDefault(v => v.Id == versionId));

        public Task AddAsync(WorkspaceVersion version, HistoryEntry record, CancellationToken cancellationToken = default)
        {
            Versions.Add(version);
            workspaces.History.Add(record);
            return Task.CompletedTask;
        }
    }
}
