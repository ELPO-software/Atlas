using System.Globalization;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace Atlas.Infrastructure.Persistence;

/// <summary>
/// Brings the database to the schema this version of Atlas expects. Existing data is copied to a backup
/// before any upgrade, and a database Atlas cannot safely use is refused and left untouched.
/// </summary>
public sealed class DatabaseMigrator
{
    /// <summary>Marks the file as belonging to Atlas: the bytes spell "ATLS".</summary>
    private const int ApplicationId = 0x41544C53;

    private const int SqliteCorrupt = 11;
    private const int SqliteNotADatabase = 26;

    private readonly SqliteDatabase _database;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DatabaseMigrator> _logger;
    private readonly IReadOnlyList<Migration> _migrations;

    public DatabaseMigrator(SqliteDatabase database, TimeProvider timeProvider, ILogger<DatabaseMigrator> logger)
        : this(database, timeProvider, logger, EmbeddedMigrations.Load())
    {
    }

    internal DatabaseMigrator(
        SqliteDatabase database,
        TimeProvider timeProvider,
        ILogger<DatabaseMigrator> logger,
        IReadOnlyList<Migration> migrations)
    {
        ThrowIfNotSequential(migrations);
        _database = database;
        _timeProvider = timeProvider;
        _logger = logger;
        _migrations = migrations;
    }

    /// <returns>The path of the backup made before upgrading, or null if no data needed one.</returns>
    public string? Migrate()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_database.DatabasePath)!);

        try
        {
            using var connection = _database.Open();
            var currentVersion = ReadCurrentVersion(connection);
            var latestVersion = _migrations[^1].Version;

            if (currentVersion == latestVersion)
                return null;

            var backupPath = currentVersion > 0 ? BackUp(connection, currentVersion) : null;

            foreach (var migration in _migrations.Where(m => m.Version > currentVersion))
                Apply(connection, migration);

            return backupPath;
        }
        catch (SqliteException ex)
        {
            _logger.LogError(ex, "The database could not be read or backed up");
            throw new DatabaseUnreadableException(_database.DatabasePath, ex);
        }
    }

    private int ReadCurrentVersion(SqliteConnection connection)
    {
        var applicationId = Convert.ToInt32(Scalar(connection, "PRAGMA application_id"), CultureInfo.InvariantCulture);
        var tableCount = Convert.ToInt32(Scalar(connection, "SELECT COUNT(*) FROM sqlite_master"), CultureInfo.InvariantCulture);

        // A new file has neither an identity nor any content. Anything else must carry the Atlas identity.
        if (applicationId != ApplicationId && (applicationId != 0 || tableCount > 0))
            throw new ForeignDatabaseException(_database.DatabasePath);

        var version = Convert.ToInt32(Scalar(connection, "PRAGMA user_version"), CultureInfo.InvariantCulture);
        var latestVersion = _migrations[^1].Version;
        if (version > latestVersion)
            throw new DatabaseTooNewException(_database.DatabasePath, version, latestVersion);

        return version;
    }

    private string BackUp(SqliteConnection connection, int currentVersion)
    {
        var timestamp = _timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var backupPath = $"{_database.DatabasePath}.v{currentVersion}.{timestamp}.bak";

        var builder = new SqliteConnectionStringBuilder { DataSource = backupPath, Pooling = false };
        using (var destination = new SqliteConnection(builder.ToString()))
        {
            destination.Open();
            connection.BackupDatabase(destination);
        }

        _logger.LogInformation("Database backed up to {BackupPath} before upgrading from version {Version}", backupPath, currentVersion);
        return backupPath;
    }

    private void Apply(SqliteConnection connection, Migration migration)
    {
        try
        {
            using var transaction = connection.BeginTransaction();

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = migration.Sql;
                command.ExecuteNonQuery();
            }

            using (var command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = FormattableString.Invariant(
                    $"PRAGMA application_id = {ApplicationId}; PRAGMA user_version = {migration.Version};");
                command.ExecuteNonQuery();
            }

            transaction.Commit();
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is not (SqliteCorrupt or SqliteNotADatabase))
        {
            _logger.LogError(ex, "Upgrading the database to version {Version} failed", migration.Version);
            throw new DatabaseMigrationException(_database.DatabasePath, migration.Version, ex);
        }

        _logger.LogInformation("Database upgraded to version {Version} ({Name})", migration.Version, migration.Name);
    }

    private static object? Scalar(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar();
    }

    private static void ThrowIfNotSequential(IReadOnlyList<Migration> migrations)
    {
        if (migrations.Count == 0)
            throw new InvalidOperationException("Atlas has no database migrations.");

        for (var i = 0; i < migrations.Count; i++)
        {
            if (migrations[i].Version != i + 1)
                throw new InvalidOperationException($"Migration {migrations[i].Version} is out of sequence: expected {i + 1}.");
        }
    }
}
