using Atlas.Infrastructure.Persistence;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Atlas.Infrastructure.Tests;

public sealed class DatabaseMigratorTests : IDisposable
{
    private const int AtlasApplicationId = 0x41544C53;

    private static readonly Migration CreateNotes = new(1, "create_notes", "CREATE TABLE notes (text TEXT NOT NULL) STRICT;");

    private readonly TemporaryFolder _folder = new();
    private readonly FakeTimeProvider _clock = new(new DateTimeOffset(2026, 11, 4, 10, 0, 0, TimeSpan.Zero));

    public void Dispose() => _folder.Dispose();

    [Fact]
    public void Migrate_CreatesTheSchemaOnANewDatabaseWithoutABackup()
    {
        var migrator = new DatabaseMigrator(new SqliteDatabase(_folder.DatabasePath), _clock, NullLogger<DatabaseMigrator>.Instance);

        var backup = migrator.Migrate();

        Assert.Null(backup);
        Assert.Equal(EmbeddedMigrations.Load()[^1].Version, Pragma("user_version"));
        Assert.Equal(AtlasApplicationId, Pragma("application_id"));
        Assert.Equal(1, Query("SELECT COUNT(*) FROM sqlite_master WHERE name = 'workspaces'"));
        Assert.Equal(1, Query("SELECT COUNT(*) FROM sqlite_master WHERE name = 'resources'"));
        Assert.Single(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public void Migrate_CreatesAMissingDataFolder()
    {
        var path = Path.Combine(_folder.Path, "sous dossier", "été", "atlas.db");
        var migrator = new DatabaseMigrator(new SqliteDatabase(path), _clock, NullLogger<DatabaseMigrator>.Instance);

        migrator.Migrate();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Migrate_DoesNothingWhenTheDatabaseIsCurrent()
    {
        var migrator = new DatabaseMigrator(new SqliteDatabase(_folder.DatabasePath), _clock, NullLogger<DatabaseMigrator>.Instance);
        migrator.Migrate();

        var backup = migrator.Migrate();

        Assert.Null(backup);
        Assert.Single(Directory.GetFiles(_folder.Path));
    }

    [Fact]
    public void Migrate_AppliesOnlyThePendingScriptsAndKeepsTheData()
    {
        Migrator(CreateNotes).Migrate();
        Execute("INSERT INTO notes VALUES ('à garder')");

        Migrator(CreateNotes, new Migration(2, "add_tags", "CREATE TABLE tags (text TEXT NOT NULL) STRICT;")).Migrate();

        Assert.Equal(2, Pragma("user_version"));
        Assert.Equal(1, Query("SELECT COUNT(*) FROM notes WHERE text = 'à garder'"));
        Assert.Equal(1, Query("SELECT COUNT(*) FROM sqlite_master WHERE name = 'tags'"));
    }

    [Fact]
    public void Migrate_BacksUpExistingDataBeforeAnUpgrade()
    {
        Migrator(CreateNotes).Migrate();
        Execute("INSERT INTO notes VALUES ('à garder')");

        var backup = Migrator(CreateNotes, new Migration(2, "drop_notes", "DROP TABLE notes;")).Migrate();

        Assert.NotNull(backup);
        Assert.StartsWith(_folder.DatabasePath + ".v1.", backup);
        Assert.Equal(1, Query("SELECT COUNT(*) FROM notes WHERE text = 'à garder'", backup));
        Assert.Equal(1, Query("PRAGMA user_version", backup));
    }

    [Fact]
    public void Migrate_RollsBackAFailedUpgradeCompletely()
    {
        Migrator(CreateNotes).Migrate();
        Execute("INSERT INTO notes VALUES ('à garder')");
        var broken = new Migration(2, "broken", "CREATE TABLE tags (text TEXT) STRICT; THIS IS NOT SQL;");

        var exception = Assert.Throws<DatabaseMigrationException>(() => Migrator(CreateNotes, broken).Migrate());

        Assert.Equal(2, exception.Version);
        Assert.Equal(1, Pragma("user_version"));
        Assert.Equal(0, Query("SELECT COUNT(*) FROM sqlite_master WHERE name = 'tags'"));
        Assert.Equal(1, Query("SELECT COUNT(*) FROM notes WHERE text = 'à garder'"));
    }

    [Fact]
    public void Migrate_RefusesADatabaseFromANewerVersionAndLeavesItUntouched()
    {
        Migrator(CreateNotes).Migrate();
        Execute("PRAGMA user_version = 99");
        var before = File.ReadAllBytes(_folder.DatabasePath);

        var exception = Assert.Throws<DatabaseTooNewException>(() => Migrator(CreateNotes).Migrate());

        Assert.Equal(99, exception.FoundVersion);
        Assert.Equal(1, exception.SupportedVersion);
        Assert.Equal(_folder.DatabasePath, exception.DatabasePath);
        Assert.Equal(before, File.ReadAllBytes(_folder.DatabasePath));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(12345)]
    public void Migrate_RefusesADatabaseThatBelongsToAnotherProgram(int applicationId)
    {
        Execute($"PRAGMA application_id = {applicationId}; CREATE TABLE theirs (value TEXT);");
        var before = File.ReadAllBytes(_folder.DatabasePath);

        Assert.Throws<ForeignDatabaseException>(() => Migrator(CreateNotes).Migrate());

        Assert.Equal(before, File.ReadAllBytes(_folder.DatabasePath));
    }

    [Fact]
    public void Migrate_RefusesAFileThatIsNotADatabaseAndLeavesItUntouched()
    {
        const string Content = "Ceci n'est pas une base de données, mais un fichier suffisamment long pour être lu comme tel.";
        File.WriteAllText(_folder.DatabasePath, Content);

        Assert.Throws<DatabaseUnreadableException>(() => Migrator(CreateNotes).Migrate());

        Assert.Equal(Content, File.ReadAllText(_folder.DatabasePath));
    }

    [Fact]
    public void Constructor_RefusesAGapInTheVersions() =>
        Assert.Throws<InvalidOperationException>(() => Migrator(CreateNotes, new Migration(3, "gap", "SELECT 1;")));

    [Fact]
    public void Constructor_RefusesAnEmptyList() =>
        Assert.Throws<InvalidOperationException>(() => Migrator());

    [Fact]
    public void EmbeddedMigrations_StartAtOneAndAreSequential()
    {
        var versions = EmbeddedMigrations.Load().Select(m => m.Version).ToList();

        Assert.Equal(Enumerable.Range(1, versions.Count), versions);
    }

    private DatabaseMigrator Migrator(params Migration[] migrations) =>
        new(new SqliteDatabase(_folder.DatabasePath), _clock, NullLogger<DatabaseMigrator>.Instance, migrations);

    private void Execute(string sql)
    {
        using var connection = OpenConnection(_folder.DatabasePath);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private int Pragma(string name) => Query($"PRAGMA {name}");

    private int Query(string sql, string? databasePath = null)
    {
        using var connection = OpenConnection(databasePath ?? _folder.DatabasePath);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return Convert.ToInt32(command.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    private static SqliteConnection OpenConnection(string path)
    {
        var connection = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = path, Pooling = false }.ToString());
        connection.Open();
        return connection;
    }
}
