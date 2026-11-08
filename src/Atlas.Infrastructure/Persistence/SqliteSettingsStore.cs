using Atlas.Core.Settings;

namespace Atlas.Infrastructure.Persistence;

public sealed class SqliteSettingsStore(SqliteDatabase database) : ISettingsStore
{
    public Task<string?> GetAsync(string key, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT value FROM settings WHERE key = $key";
                command.Parameters.AddWithValue("$key", key);
                return (string?)command.ExecuteScalar();
            },
            cancellationToken);

    public Task SetAsync(string key, string value, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO settings (key, value) VALUES ($key, $value)
                    ON CONFLICT (key) DO UPDATE SET value = excluded.value
                    """;
                command.Parameters.AddWithValue("$key", key);
                command.Parameters.AddWithValue("$value", value);
                return command.ExecuteNonQuery();
            },
            cancellationToken);
}
