using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;
using Microsoft.Extensions.Time.Testing;

namespace Atlas.Core.Tests;

public sealed class WorkspaceServiceTests
{
    private readonly InMemoryWorkspaceRepository _repository = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));
    private readonly WorkspaceService _service;

    public WorkspaceServiceTests() => _service = new WorkspaceService(_repository, _clock);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task CreateAsync_SavesTheWorkspaceWithTheCurrentDate()
    {
        var created = await _service.CreateAsync("Études", "Semestre d'été", Token);

        var stored = await _service.GetAsync(created.Id, Token);
        Assert.Equal("Études", stored.Name);
        Assert.Equal("Semestre d'été", stored.Description);
        Assert.Equal(_clock.GetUtcNow(), stored.CreatedAt);
    }

    [Fact]
    public async Task CreateAsync_WritesNothingWhenTheNameIsRefused()
    {
        await Assert.ThrowsAsync<AtlasValidationException>(() => _service.CreateAsync(" ", null, Token));

        Assert.Equal(0, _repository.WriteCount);
        Assert.Equal(0, await _service.CountAsync(Token));
    }

    [Fact]
    public async Task GetAsync_ThrowsWhenTheWorkspaceDoesNotExist()
    {
        var id = Guid.NewGuid();

        var exception = await Assert.ThrowsAsync<WorkspaceNotFoundException>(() => _service.GetAsync(id, Token));

        Assert.Equal(id, exception.WorkspaceId);
    }

    [Fact]
    public async Task ListAsync_ReturnsTheMostRecentlyModifiedFirst()
    {
        var older = await _service.CreateAsync("Ancien", null, Token);
        _clock.Advance(TimeSpan.FromMinutes(1));
        var newer = await _service.CreateAsync("Récent", null, Token);

        var list = await _service.ListAsync(Token);

        Assert.Equal([newer.Id, older.Id], list.Select(w => w.Id));
    }

    [Fact]
    public async Task UpdateDetailsAsync_SavesTheChangeWithTheCurrentDate()
    {
        var created = await _service.CreateAsync("Avant", null, Token);
        _clock.Advance(TimeSpan.FromHours(1));

        await _service.UpdateDetailsAsync(created.Id, "Après", "Description", Token);

        var stored = await _service.GetAsync(created.Id, Token);
        Assert.Equal("Après", stored.Name);
        Assert.Equal("Description", stored.Description);
        Assert.Equal(created.CreatedAt, stored.CreatedAt);
        Assert.Equal(_clock.GetUtcNow(), stored.UpdatedAt);
    }

    [Fact]
    public async Task UpdateDetailsAsync_SavesNothingWhenRefused()
    {
        var created = await _service.CreateAsync("Avant", null, Token);

        await Assert.ThrowsAsync<AtlasValidationException>(() => _service.UpdateDetailsAsync(created.Id, " ", null, Token));

        Assert.Equal("Avant", (await _service.GetAsync(created.Id, Token)).Name);
    }

    [Fact]
    public async Task UpdateDetailsAsync_ThrowsWhenTheWorkspaceDoesNotExist() =>
        await Assert.ThrowsAsync<WorkspaceNotFoundException>(() => _service.UpdateDetailsAsync(Guid.NewGuid(), "Nom", null, Token));

    [Fact]
    public async Task UpdateNotesAsync_ReportsAWorkspaceThatDisappearedDuringTheOperation()
    {
        var created = await _service.CreateAsync("Nom", null, Token);
        _repository.LoseWorkspaceBeforeUpdate = true;

        await Assert.ThrowsAsync<WorkspaceNotFoundException>(() => _service.UpdateNotesAsync(created.Id, "Notes", Token));
    }

    [Fact]
    public async Task UpdateNotesAsync_SavesTheNotes()
    {
        var created = await _service.CreateAsync("Nom", null, Token);

        await _service.UpdateNotesAsync(created.Id, "À faire :\n- relire", Token);

        Assert.Equal("À faire :\n- relire", (await _service.GetAsync(created.Id, Token)).Notes);
    }

    [Fact]
    public async Task DuplicateAsync_SavesAnIndependentCopyAndKeepsTheSource()
    {
        var source = await _service.CreateAsync("Source", null, Token);
        await _service.UpdateNotesAsync(source.Id, "Notes", Token);
        _clock.Advance(TimeSpan.FromHours(1));

        var copy = await _service.DuplicateAsync(source.Id, "Copie de Source", Token);

        Assert.Equal(2, await _service.CountAsync(Token));
        Assert.NotEqual(source.Id, copy.Id);
        Assert.Equal("Notes", (await _service.GetAsync(copy.Id, Token)).Notes);
        Assert.Equal("Source", (await _service.GetAsync(source.Id, Token)).Name);
        Assert.Equal(_clock.GetUtcNow(), copy.CreatedAt);
    }

    [Fact]
    public async Task DuplicateAsync_ThrowsWhenTheWorkspaceDoesNotExist() =>
        await Assert.ThrowsAsync<WorkspaceNotFoundException>(() => _service.DuplicateAsync(Guid.NewGuid(), "Copie", Token));

    [Fact]
    public async Task DeleteAsync_RemovesOnlyTheNamedWorkspace()
    {
        var kept = await _service.CreateAsync("Gardé", null, Token);
        var removed = await _service.CreateAsync("Supprimé", null, Token);

        await _service.DeleteAsync(removed.Id, Token);

        Assert.Equal(kept.Id, Assert.Single(await _service.ListAsync(Token)).Id);
    }

    [Fact]
    public async Task DeleteAsync_ThrowsWhenTheWorkspaceDoesNotExist() =>
        await Assert.ThrowsAsync<WorkspaceNotFoundException>(() => _service.DeleteAsync(Guid.NewGuid(), Token));

    [Fact]
    public async Task UpdateDetailsAsync_KeepsTheResources()
    {
        var workspace = Workspace.Create("Nom", null, _clock.GetUtcNow());
        var resource = workspace.AddResource(ResourceKind.Url, "Lien", "https://example.org", _clock.GetUtcNow());
        await _repository.AddAsync(workspace, Token);

        await _service.UpdateDetailsAsync(workspace.Id, "Nouveau nom", null, Token);

        Assert.Equal(resource, Assert.Single((await _service.GetAsync(workspace.Id, Token)).Resources));
    }
}
