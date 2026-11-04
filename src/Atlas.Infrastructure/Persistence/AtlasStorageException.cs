namespace Atlas.Infrastructure.Persistence;

/// <summary>Atlas cannot safely use the database. The file is left exactly as it was found.</summary>
public abstract class AtlasStorageException(string message, string databasePath, Exception? innerException = null)
    : Exception(message, innerException)
{
    public string DatabasePath { get; } = databasePath;
}

/// <summary>The database was written by a newer version of Atlas, whose data this version could damage.</summary>
public sealed class DatabaseTooNewException(string databasePath, int foundVersion, int supportedVersion)
    : AtlasStorageException(
        $"The database is at version {foundVersion}; this version of Atlas supports up to {supportedVersion}.",
        databasePath)
{
    public int FoundVersion { get; } = foundVersion;

    public int SupportedVersion { get; } = supportedVersion;
}

/// <summary>The file is a valid SQLite database that belongs to another program.</summary>
public sealed class ForeignDatabaseException(string databasePath)
    : AtlasStorageException("The file is a SQLite database that was not created by Atlas.", databasePath);

/// <summary>The file is damaged, is not a SQLite database, or cannot be accessed.</summary>
public sealed class DatabaseUnreadableException(string databasePath, Exception innerException)
    : AtlasStorageException("The database file could not be read.", databasePath, innerException);

/// <summary>An upgrade failed and was rolled back. The database and its backup are intact.</summary>
public sealed class DatabaseMigrationException(string databasePath, int version, Exception innerException)
    : AtlasStorageException($"Upgrading the database to version {version} failed.", databasePath, innerException)
{
    public int Version { get; } = version;
}
