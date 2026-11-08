using System.Globalization;
using Atlas.Core.History;
using Atlas.Core.Versions;
using Microsoft.Data.Sqlite;

namespace Atlas.Infrastructure.Persistence;

public sealed class SqliteVersionRepository(SqliteDatabase database) : IVersionRepository
{
    public Task<IReadOnlyList<VersionSummary>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        database.RunAsync<IReadOnlyList<VersionSummary>>(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT id, name, saved_at, is_automatic, resource_count
                    FROM versions WHERE workspace_id = $workspace_id
                    ORDER BY saved_at DESC, rowid DESC
                    """;
                command.Parameters.AddWithValue("$workspace_id", workspaceId.ToString("D"));

                using var reader = command.ExecuteReader();
                var versions = new List<VersionSummary>();
                while (reader.Read())
                {
                    versions.Add(new VersionSummary(
                        Guid.Parse(reader.GetString(0)),
                        workspaceId,
                        reader.GetString(1),
                        ReadDate(reader, 2),
                        reader.GetBoolean(3),
                        reader.GetInt32(4)));
                }

                return versions;
            },
            cancellationToken);

    public Task<int> CountAsync(Guid workspaceId, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM versions WHERE workspace_id = $workspace_id";
                command.Parameters.AddWithValue("$workspace_id", workspaceId.ToString("D"));
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            },
            cancellationToken);

    public Task<WorkspaceVersion?> GetAsync(Guid versionId, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT workspace_id, name, saved_at, is_automatic, resource_count, content
                    FROM versions WHERE id = $id
                    """;
                command.Parameters.AddWithValue("$id", versionId.ToString("D"));

                using var reader = command.ExecuteReader();
                return reader.Read()
                    ? new WorkspaceVersion(
                        versionId,
                        Guid.Parse(reader.GetString(0)),
                        reader.GetString(1),
                        ReadDate(reader, 2),
                        reader.GetBoolean(3),
                        reader.GetInt32(4),
                        reader.GetString(5))
                    : null;
            },
            cancellationToken);

    public Task AddAsync(WorkspaceVersion version, HistoryEntry record, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction();
                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO versions (id, workspace_id, name, saved_at, is_automatic, resource_count, content)
                        VALUES ($id, $workspace_id, $name, $saved_at, $is_automatic, $resource_count, $content)
                        """;
                    command.Parameters.AddWithValue("$id", version.Id.ToString("D"));
                    command.Parameters.AddWithValue("$workspace_id", version.WorkspaceId.ToString("D"));
                    command.Parameters.AddWithValue("$name", version.Name);
                    command.Parameters.AddWithValue("$saved_at", HistoryTable.Text(version.SavedAt));
                    command.Parameters.AddWithValue("$is_automatic", version.IsAutomatic ? 1 : 0);
                    command.Parameters.AddWithValue("$resource_count", version.ResourceCount);
                    command.Parameters.AddWithValue("$content", version.Content);
                    command.ExecuteNonQuery();
                }

                HistoryTable.Write(connection, transaction, record);
                transaction.Commit();
                return true;
            },
            cancellationToken);

    private static DateTimeOffset ReadDate(SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
