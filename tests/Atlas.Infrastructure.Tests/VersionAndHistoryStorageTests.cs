using System.Text;
using Atlas.Core.Exchange;
using Atlas.Core.History;
using Atlas.Core.Resources;
using Atlas.Core.Versions;
using Atlas.Core.Workspaces;
using Atlas.Infrastructure.Persistence;
using Atlas.Infrastructure.Storage;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Infrastructure.Tests;

public sealed class VersionAndHistoryStorageTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 10, 0, 0, TimeSpan.Zero);

    private readonly TemporaryFolder _folder = new();
    private readonly SqliteDatabase _database;
    private readonly SqliteWorkspaceRepository _workspaces;
    private readonly SqliteVersionRepository _versions;
    private readonly SqliteHistoryRepository _history;

    public VersionAndHistoryStorageTests()
    {
        _database = new SqliteDatabase(_folder.DatabasePath);
        new DatabaseMigrator(_database, TimeProvider.System, NullLogger<DatabaseMigrator>.Instance).Migrate();
        _workspaces = new SqliteWorkspaceRepository(_database);
        _versions = new SqliteVersionRepository(_database);
        _history = new SqliteHistoryRepository(_database);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task History_IsWrittenWithTheChangeAndReadBackMostRecentFirst()
    {
        var workspace = Workspace.Create("Études d'été", null, Now);
        await _workspaces.AddAsync(workspace, Entry(HistoryAction.WorkspaceCreated, workspace, Now), Token);
        workspace.MarkUsed(Now.AddHours(1));
        await _workspaces.UpdateAsync(workspace, Entry(HistoryAction.WorkspaceRestored, workspace, Now.AddHours(1), counts: new RestoreCounts(3, 1, 0, 2)), Token);

        var entries = await _history.ListAsync(10, Token);

        Assert.Equal([HistoryAction.WorkspaceRestored, HistoryAction.WorkspaceCreated], entries.Select(e => e.Action));
        Assert.Equal(new RestoreCounts(3, 1, 0, 2), entries[0].Counts);
        Assert.Null(entries[1].Counts);
        Assert.Equal(("Études d'été", workspace.Id, Now), (entries[1].WorkspaceName, entries[1].WorkspaceId, entries[1].OccurredAt));
    }

    [Fact]
    public async Task History_OutlivesTheWorkspace()
    {
        var workspace = Workspace.Create("Supprimé", null, Now);
        await _workspaces.AddAsync(workspace, Entry(HistoryAction.WorkspaceCreated, workspace, Now), Token);

        await _workspaces.DeleteAsync(workspace.Id, Entry(HistoryAction.WorkspaceDeleted, workspace, Now.AddMinutes(5)), Token);

        Assert.Equal(
            [HistoryAction.WorkspaceDeleted, HistoryAction.WorkspaceCreated],
            (await _history.ListAsync(10, Token)).Select(e => e.Action));
    }

    [Fact]
    public async Task History_IsNotWrittenWhenTheChangeFails()
    {
        var workspace = Workspace.Create("Absent", null, Now);

        Assert.False(await _workspaces.UpdateAsync(workspace, Entry(HistoryAction.WorkspaceEdited, workspace, Now), Token));
        Assert.False(await _workspaces.DeleteAsync(workspace.Id, Entry(HistoryAction.WorkspaceDeleted, workspace, Now), Token));

        Assert.Empty(await _history.ListAsync(10, Token));
    }

    [Fact]
    public async Task History_IsRolledBackWithAChangeThatBreaksHalfway()
    {
        var workspace = Workspace.Create("Nom", null, Now);
        var id = Guid.NewGuid();
        var broken = Workspace.Restore(
            workspace.Id, "Nom", string.Empty, string.Empty, Now, Now, null,
            [
                new Resource(id, ResourceKind.Url, "A", "https://example.org/a", 0, false, Now, Now),
                new Resource(id, ResourceKind.Url, "B", "https://example.org/b", 1, false, Now, Now),
            ]);

        await Assert.ThrowsAsync<SqliteException>(() => _workspaces.AddAsync(broken, Entry(HistoryAction.WorkspaceCreated, broken, Now), Token));

        Assert.Empty(await _history.ListAsync(10, Token));
    }

    [Fact]
    public async Task History_MergesNotesSavedCloseTogether()
    {
        var workspace = Workspace.Create("Nom", null, Now);
        await _workspaces.AddAsync(workspace, cancellationToken: Token);

        await _history.RecordAsync(Entry(HistoryAction.NotesEdited, workspace, Now), Token);
        await _history.RecordAsync(Entry(HistoryAction.NotesEdited, workspace, Now.AddMinutes(10)), Token);
        await _history.RecordAsync(Entry(HistoryAction.NotesEdited, workspace, Now.AddMinutes(30)), Token);

        var entry = Assert.Single(await _history.ListAsync(10, Token));
        Assert.Equal(Now.AddMinutes(30), entry.OccurredAt);
    }

    [Fact]
    public async Task History_KeepsNotesSavedFarApartOrBetweenOtherActions()
    {
        var workspace = Workspace.Create("Nom", null, Now);
        var other = Workspace.Create("Autre", null, Now);

        await _history.RecordAsync(Entry(HistoryAction.NotesEdited, workspace, Now), Token);
        await _history.RecordAsync(Entry(HistoryAction.NotesEdited, workspace, Now.AddMinutes(31)), Token);
        await _history.RecordAsync(Entry(HistoryAction.NotesEdited, other, Now.AddMinutes(32)), Token);
        await _history.RecordAsync(Entry(HistoryAction.WorkspaceEdited, workspace, Now.AddMinutes(33)), Token);
        await _history.RecordAsync(Entry(HistoryAction.NotesEdited, workspace, Now.AddMinutes(34)), Token);

        Assert.Equal(5, (await _history.ListAsync(10, Token)).Count);
    }

    [Fact]
    public async Task History_ListsOnlyAsManyLinesAsAsked()
    {
        var workspace = Workspace.Create("Nom", null, Now);
        for (var i = 0; i < 5; i++)
            await _history.RecordAsync(Entry(HistoryAction.WorkspaceEdited, workspace, Now.AddMinutes(i)), Token);

        var entries = await _history.ListAsync(3, Token);

        Assert.Equal([Now.AddMinutes(4), Now.AddMinutes(3), Now.AddMinutes(2)], entries.Select(e => e.OccurredAt));
    }

    [Fact]
    public async Task History_LeavesLinesFromANewerVersionInPlace()
    {
        var workspace = Workspace.Create("Nom", null, Now);
        await _history.RecordAsync(Entry(HistoryAction.WorkspaceCreated, workspace, Now), Token);
        Execute($"""
            INSERT INTO history (occurred_at, action, workspace_id, workspace_name, detail)
            VALUES ('{Now.AddMinutes(1):O}', 'action-future', '{workspace.Id}', 'Nom', '')
            """);

        Assert.Equal([HistoryAction.WorkspaceCreated], (await _history.ListAsync(10, Token)).Select(e => e.Action));
        Assert.Equal(2, Count("SELECT COUNT(*) FROM history"));
    }

    [Fact]
    public async Task Versions_AreStoredListedAndReadBack()
    {
        var workspace = Workspace.Create("Études", null, Now);
        await _workspaces.AddAsync(workspace, cancellationToken: Token);
        var older = Version(workspace, "Première", Now, isAutomatic: false);
        var newer = Version(workspace, "Avant le retour", Now.AddMinutes(1), isAutomatic: true);

        await _versions.AddAsync(older, Entry(HistoryAction.VersionSaved, workspace, Now, "Première"), Token);
        await _versions.AddAsync(newer, Entry(HistoryAction.VersionSaved, workspace, Now.AddMinutes(1), "Avant le retour"), Token);

        var list = await _versions.ListAsync(workspace.Id, Token);
        Assert.Equal([("Avant le retour", true), ("Première", false)], list.Select(v => (v.Name, v.IsAutomatic)));
        Assert.Equal(2, await _versions.CountAsync(workspace.Id, Token));
        Assert.Equal(older, await _versions.GetAsync(older.Id, Token));
        Assert.Equal(2, (await _history.ListAsync(10, Token)).Count);
    }

    [Fact]
    public async Task Versions_GoWithTheirWorkspaceButTheHistoryStays()
    {
        var workspace = Workspace.Create("Études", null, Now);
        await _workspaces.AddAsync(workspace, cancellationToken: Token);
        await _versions.AddAsync(Version(workspace, "Première", Now, false), Entry(HistoryAction.VersionSaved, workspace, Now, "Première"), Token);

        await _workspaces.DeleteAsync(workspace.Id, cancellationToken: Token);

        Assert.Equal(0, await _versions.CountAsync(workspace.Id, Token));
        Assert.Single(await _history.ListAsync(10, Token));
    }

    [Fact]
    public async Task Versions_OfAnotherWorkspaceAreNotListed()
    {
        var first = Workspace.Create("Premier", null, Now);
        var second = Workspace.Create("Second", null, Now);
        await _workspaces.AddAsync(first, cancellationToken: Token);
        await _workspaces.AddAsync(second, cancellationToken: Token);
        await _versions.AddAsync(Version(first, "Version", Now, false), Entry(HistoryAction.VersionSaved, first, Now), Token);

        Assert.Empty(await _versions.ListAsync(second.Id, Token));
    }

    [Fact]
    public async Task Files_AreReadAsTextWithoutTheirByteOrderMark()
    {
        var path = Path.Combine(_folder.Path, "espace d'été.json");
        await File.WriteAllTextAsync(path, "{ \"é\": 1 }", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), Token);

        var read = await WorkspaceFiles.ReadForImportAsync(path, Token);

        Assert.Equal("{ \"é\": 1 }", read.Text);
        Assert.Null(read.Problem);
    }

    [Fact]
    public async Task Files_LargerThanTheLimitAreRefusedFromTheirSize()
    {
        var path = Path.Combine(_folder.Path, "énorme.json");
        await using (var file = File.Create(path))
            file.SetLength(WorkspaceDocument.MaxBytes + 1);

        Assert.Equal(DocumentProblem.TooLarge, (await WorkspaceFiles.ReadForImportAsync(path, Token)).Problem);
    }

    [Fact]
    public async Task Files_ThatAreNotTextAreRefused()
    {
        var path = Path.Combine(_folder.Path, "image.json");
        await File.WriteAllBytesAsync(path, [0x7B, 0xFF, 0xFE, 0x00, 0x7D], Token);

        Assert.Equal(DocumentProblem.NotText, (await WorkspaceFiles.ReadForImportAsync(path, Token)).Problem);
    }

    [Fact]
    public async Task Files_ThatDoNotExistAreReported() =>
        Assert.Equal(DocumentProblem.FileNotFound, (await WorkspaceFiles.ReadForImportAsync(Path.Combine(_folder.Path, "absent.json"), Token)).Problem);

    [Fact]
    public async Task Files_AreWrittenWholeAndReplaceAnOlderOne()
    {
        var exports = Directory.CreateDirectory(Path.Combine(_folder.Path, "Exports d'été")).FullName;
        var path = Path.Combine(exports, "Études d'été.json");
        await File.WriteAllTextAsync(path, "ancien contenu, plus long que le nouveau", Token);

        await WorkspaceFiles.WriteAsync(path, "{ \"nom\": \"Études\" }", Token);

        Assert.Equal("{ \"nom\": \"Études\" }", await File.ReadAllTextAsync(path, Token));
        Assert.Equal([path], Directory.GetFiles(exports));
    }

    [Fact]
    public async Task Files_InAFolderThatDoesNotExistAreNotCreated()
    {
        var path = Path.Combine(_folder.Path, "absent", "espace.json");

        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => WorkspaceFiles.WriteAsync(path, "{}", Token));

        Assert.False(Directory.Exists(Path.Combine(_folder.Path, "absent")));
    }

    private static HistoryEntry Entry(
        HistoryAction action, Workspace workspace, DateTimeOffset at, string detail = "", RestoreCounts? counts = null) =>
        new(at, action, workspace.Id, workspace.Name, detail, counts);

    private static WorkspaceVersion Version(Workspace workspace, string name, DateTimeOffset at, bool isAutomatic) =>
        new(Guid.NewGuid(), workspace.Id, name, at, isAutomatic, workspace.Resources.Count, WorkspaceDocument.Write(workspace, at));

    private void Execute(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _folder.DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private int Count(string sql)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = _folder.DatabasePath, Pooling = false }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }
}
