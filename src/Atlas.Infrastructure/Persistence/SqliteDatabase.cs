using Microsoft.Data.Sqlite;

namespace Atlas.Infrastructure.Persistence;

/// <summary>Opens connections to the Atlas database file.</summary>
public sealed class SqliteDatabase
{
    private readonly string _connectionString;

    public SqliteDatabase(string databasePath)
    {
        DatabasePath = Path.GetFullPath(databasePath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            ForeignKeys = true,
            DefaultTimeout = 10,

            // Without pooling, the file is released as soon as an operation ends.
            Pooling = false,
        }.ToString();
    }

    public string DatabasePath { get; }

    internal SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        try
        {
            connection.Open();
            return connection;
        }
        catch
        {
            // Otherwise a failed open would keep the file locked until the next garbage collection.
            connection.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Runs database work on the thread pool. The asynchronous methods of Microsoft.Data.Sqlite complete
    /// synchronously, so awaiting them directly would freeze the interface during disk access.
    /// </summary>
    internal Task<T> RunAsync<T>(Func<SqliteConnection, T> work, CancellationToken cancellationToken) =>
        Task.Run(
            () =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                using var connection = Open();
                return work(connection);
            },
            cancellationToken);
}
