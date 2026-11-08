using Atlas.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace Atlas.Infrastructure.Tests;

public sealed class SqliteSettingsStoreTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();
    private readonly SqliteSettingsStore _store;

    public SqliteSettingsStoreTests()
    {
        var database = new SqliteDatabase(_folder.DatabasePath);
        new DatabaseMigrator(database, TimeProvider.System, NullLogger<DatabaseMigrator>.Instance).Migrate();
        _store = new SqliteSettingsStore(database);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public void Dispose() => _folder.Dispose();

    [Fact]
    public async Task GetAsync_ReturnsNullForASettingNeverSaved() =>
        Assert.Null(await _store.GetAsync("absent", Token));

    [Fact]
    public async Task SetAsync_IsReadBackWithAccents()
    {
        await _store.SetAsync("thème", "été", Token);

        Assert.Equal("été", await _store.GetAsync("thème", Token));
    }

    [Fact]
    public async Task SetAsync_ReplacesThePreviousValue()
    {
        await _store.SetAsync("theme", "light", Token);
        await _store.SetAsync("theme", "dark", Token);

        Assert.Equal("dark", await _store.GetAsync("theme", Token));
    }

    [Fact]
    public async Task SetAsync_LeavesOtherSettingsAlone()
    {
        await _store.SetAsync("premier", "1", Token);
        await _store.SetAsync("second", "2", Token);

        Assert.Equal("1", await _store.GetAsync("premier", Token));
    }

    [Fact]
    public async Task Values_SurviveANewConnectionToTheFile()
    {
        await _store.SetAsync("theme", "dark", Token);

        var reopened = new SqliteSettingsStore(new SqliteDatabase(_folder.DatabasePath));

        Assert.Equal("dark", await reopened.GetAsync("theme", Token));
    }

    [Fact]
    public async Task SpecialCharacters_AreStoredAsPlainText()
    {
        const string Hostile = "'; DROP TABLE settings; --";

        await _store.SetAsync("theme", Hostile, Token);

        Assert.Equal(Hostile, await _store.GetAsync("theme", Token));
    }
}
