using Atlas.Core.Resources;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Infrastructure.Tests;

public sealed class SqliteWorkspaceRepositoryTests : IDisposable
{
    private static readonly DateTimeOffset Created = new(2026, 11, 4, 10, 0, 0, 123, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = Created.AddHours(3);

    private readonly TemporaryFolder _folder = new();
    private readonly SqliteDatabase _database;
    private readonly SqliteWorkspaceRepository _repository;

    public SqliteWorkspaceRepositoryTests()
    {
        _database = new SqliteDatabase(_folder.DatabasePath);
        new DatabaseMigrator(_database, TimeProvider.System, NullLogger<DatabaseMigrator>.Instance).Migrate();
        _repository = new SqliteWorkspaceRepository(_database);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task AddAndGet_ReturnEveryValueExactly()
    {
        var workspace = Workspace.Create("Études d'été 日本語 🗺️", "Description\nsur deux lignes", Created);
        workspace.UpdateNotes("Objectifs :\r\n\t- tâche à finir", Created);
        workspace.AddResource(ResourceKind.Application, "Éditeur", @"C:\Program Files\Éditeur d'été\Mon Appli.exe", Created);
        workspace.AddResource(ResourceKind.File, "Résumé", @"D:\Mes Documents\Résumé final (v2).pdf", Created);
        workspace.AddResource(ResourceKind.Folder, "Projets", @"\\serveur\partage commun\Année 2026", Created);
        workspace.AddResource(ResourceKind.Url, "Documentation", "https://example.org/é?a=1&b=2#ancre", Created);
        workspace.MarkUsed(Later);

        await _repository.AddAsync(workspace, cancellationToken: Token);
        var stored = await _repository.GetAsync(workspace.Id, Token);

        Assert.NotNull(stored);
        Assert.Equal(workspace.Id, stored.Id);
        Assert.Equal(workspace.Name, stored.Name);
        Assert.Equal(workspace.Description, stored.Description);
        Assert.Equal(workspace.Notes, stored.Notes);
        Assert.Equal(workspace.CreatedAt, stored.CreatedAt);
        Assert.Equal(workspace.UpdatedAt, stored.UpdatedAt);
        Assert.Equal(Later, stored.LastUsedAt);
        Assert.Equal(workspace.Resources, stored.Resources);
    }

    [Fact]
    public async Task Get_ReturnsNullWhenTheWorkspaceDoesNotExist() =>
        Assert.Null(await _repository.GetAsync(Guid.NewGuid(), Token));

    [Fact]
    public async Task DataSurvivesReopeningTheDatabase()
    {
        var workspace = Workspace.Create("Persistant", null, Created);
        await _repository.AddAsync(workspace, cancellationToken: Token);

        var reopened = new SqliteWorkspaceRepository(new SqliteDatabase(_folder.DatabasePath));

        Assert.Equal("Persistant", (await reopened.GetAsync(workspace.Id, Token))?.Name);
    }

    [Fact]
    public async Task Dates_AreStoredAsTheSameInstantWhateverTheirOffset()
    {
        var paris = new DateTimeOffset(2026, 7, 1, 14, 30, 0, TimeSpan.FromHours(2));
        var workspace = Workspace.Create("Nom", null, paris);

        await _repository.AddAsync(workspace, cancellationToken: Token);
        var stored = await _repository.GetAsync(workspace.Id, Token);

        Assert.Equal(paris, stored?.CreatedAt);
    }

    [Fact]
    public async Task ValuesThatLookLikeSqlAreStoredAsPlainText()
    {
        const string Name = "'); DROP TABLE workspaces; --";
        var workspace = Workspace.Create(Name, null, Created);

        await _repository.AddAsync(workspace, cancellationToken: Token);

        Assert.Equal(Name, (await _repository.GetAsync(workspace.Id, Token))?.Name);
        Assert.Equal(1, await _repository.CountAsync(Token));
    }

    [Fact]
    public async Task AResourceTypeFromAFutureVersionIsKept()
    {
        var workspace = Workspace.Create("Nom", null, Created);
        workspace.AddResource(ResourceKind.Parse("future-kind"), "Inconnu", "cible", Created);

        await _repository.AddAsync(workspace, cancellationToken: Token);
        var stored = await _repository.GetAsync(workspace.Id, Token);

        Assert.Equal("future-kind", Assert.Single(stored!.Resources).Kind.Value);
    }

    [Fact]
    public async Task ThePendingApprovalIsKept()
    {
        var resource = new Resource(Guid.NewGuid(), ResourceKind.Application, "Importée", @"C:\a.exe", 0, true, Created, Created);
        var workspace = Workspace.Restore(Guid.NewGuid(), "Nom", "", "", Created, Created, null, [resource]);

        await _repository.AddAsync(workspace, cancellationToken: Token);

        Assert.True(Assert.Single((await _repository.GetAsync(workspace.Id, Token))!.Resources).RequiresReview);
    }

    [Fact]
    public async Task List_ReturnsSummariesMostRecentlyModifiedFirst()
    {
        var older = Workspace.Create("Ancien", "Description", Created);
        older.AddResource(ResourceKind.Url, "A", "https://example.org/a", Created);
        older.AddResource(ResourceKind.Url, "B", "https://example.org/b", Created);
        var newer = Workspace.Create("Récent", null, Later);
        await _repository.AddAsync(older, cancellationToken: Token);
        await _repository.AddAsync(newer, cancellationToken: Token);

        var list = await _repository.ListAsync(Token);

        Assert.Equal(
            [
                new WorkspaceSummary(newer.Id, "Récent", "", 0, Later, null),
                new WorkspaceSummary(older.Id, "Ancien", "Description", 2, Created, null),
            ],
            list);
    }

    [Fact]
    public async Task List_AndCount_AreEmptyOnANewDatabase()
    {
        Assert.Empty(await _repository.ListAsync(Token));
        Assert.Equal(0, await _repository.CountAsync(Token));
    }

    [Fact]
    public async Task Update_SavesTheDetailsAndReplacesTheResources()
    {
        var workspace = Workspace.Create("Avant", null, Created);
        var removed = workspace.AddResource(ResourceKind.Url, "Retirée", "https://example.org/a", Created);
        var changed = workspace.AddResource(ResourceKind.Url, "Modifiée", "https://example.org/b", Created);
        await _repository.AddAsync(workspace, cancellationToken: Token);

        workspace.UpdateDetails("Après", "Description", Later);
        workspace.UpdateNotes("Notes", Later);
        workspace.MarkUsed(Later);
        workspace.RemoveResource(removed.Id, Later);
        workspace.UpdateResource(changed.Id, "Nouveau nom", "https://example.org/c", Later);
        workspace.AddResource(ResourceKind.File, "Ajoutée", @"C:\d.txt", Later);

        Assert.True(await _repository.UpdateAsync(workspace, cancellationToken: Token));

        var stored = await _repository.GetAsync(workspace.Id, Token);
        Assert.Equal("Après", stored?.Name);
        Assert.Equal("Description", stored?.Description);
        Assert.Equal("Notes", stored?.Notes);
        Assert.Equal(Created, stored?.CreatedAt);
        Assert.Equal(Later, stored?.UpdatedAt);
        Assert.Equal(Later, stored?.LastUsedAt);
        Assert.Equal(workspace.Resources, stored?.Resources);
    }

    [Fact]
    public async Task Update_ReturnsFalseAndWritesNothingWhenTheWorkspaceDoesNotExist()
    {
        var missing = Workspace.Create("Absent", null, Created);
        missing.AddResource(ResourceKind.Url, "A", "https://example.org/a", Created);

        Assert.False(await _repository.UpdateAsync(missing, cancellationToken: Token));

        Assert.Equal(0, CountRows("workspaces"));
        Assert.Equal(0, CountRows("resources"));
    }

    [Fact]
    public async Task Delete_RemovesTheWorkspaceAndItsResourcesOnly()
    {
        var removed = Workspace.Create("Supprimé", null, Created);
        removed.AddResource(ResourceKind.Url, "A", "https://example.org/a", Created);
        var kept = Workspace.Create("Gardé", null, Created);
        var keptResource = kept.AddResource(ResourceKind.Url, "B", "https://example.org/b", Created);
        await _repository.AddAsync(removed, cancellationToken: Token);
        await _repository.AddAsync(kept, cancellationToken: Token);

        Assert.True(await _repository.DeleteAsync(removed.Id, cancellationToken: Token));

        Assert.Null(await _repository.GetAsync(removed.Id, Token));
        Assert.Equal(keptResource, Assert.Single((await _repository.GetAsync(kept.Id, Token))!.Resources));
        Assert.Equal(1, CountRows("resources"));
    }

    [Fact]
    public async Task Delete_ReturnsFalseWhenTheWorkspaceDoesNotExist() =>
        Assert.False(await _repository.DeleteAsync(Guid.NewGuid(), cancellationToken: Token));

    [Fact]
    public async Task Add_WritesNothingWhenAResourceCannotBeSaved()
    {
        var id = Guid.NewGuid();
        var first = new Resource(id, ResourceKind.Url, "A", "a", 0, false, Created, Created);
        var duplicate = new Resource(id, ResourceKind.Url, "B", "b", 1, false, Created, Created);
        var workspace = Workspace.Restore(Guid.NewGuid(), "Nom", "", "", Created, Created, null, [first, duplicate]);

        await Assert.ThrowsAsync<SqliteException>(() => _repository.AddAsync(workspace, cancellationToken: Token));

        Assert.Equal(0, CountRows("workspaces"));
        Assert.Equal(0, CountRows("resources"));
    }

    [Fact]
    public async Task Update_KeepsThePreviousContentWhenAResourceCannotBeSaved()
    {
        var workspace = Workspace.Create("Avant", null, Created);
        var original = workspace.AddResource(ResourceKind.Url, "A", "https://example.org/a", Created);
        await _repository.AddAsync(workspace, cancellationToken: Token);

        var id = Guid.NewGuid();
        var broken = Workspace.Restore(
            workspace.Id, "Après", "", "", Created, Later, null,
            [
                new Resource(id, ResourceKind.Url, "B", "b", 0, false, Created, Later),
                new Resource(id, ResourceKind.Url, "C", "c", 1, false, Created, Later),
            ]);

        await Assert.ThrowsAsync<SqliteException>(() => _repository.UpdateAsync(broken, cancellationToken: Token));

        var stored = await _repository.GetAsync(workspace.Id, Token);
        Assert.Equal("Avant", stored?.Name);
        Assert.Equal(original, Assert.Single(stored!.Resources));
    }

    [Fact]
    public async Task ACancelledOperationWritesNothing()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => _repository.AddAsync(Workspace.Create("Nom", null, Created), cancellationToken: cancelled.Token));

        Assert.Equal(0, CountRows("workspaces"));
    }

    [Fact]
    public async Task TheFileIsReleasedAfterEachOperation()
    {
        await _repository.AddAsync(Workspace.Create("Nom", null, Created), cancellationToken: Token);

        using var exclusive = File.Open(_folder.DatabasePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.True(exclusive.Length > 0);
    }

    private int CountRows(string table)
    {
        using var connection = _database.Open();
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM {table}";
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
