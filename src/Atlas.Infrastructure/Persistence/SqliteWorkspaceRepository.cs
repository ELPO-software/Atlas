using System.Globalization;
using Atlas.Core.Resources;
using Atlas.Core.Workspaces;
using Microsoft.Data.Sqlite;

namespace Atlas.Infrastructure.Persistence;

public sealed class SqliteWorkspaceRepository(SqliteDatabase database) : IWorkspaceRepository
{
    private const string SelectSummaries = """
        SELECT w.id, w.name, w.description, COUNT(r.id), w.updated_at, w.last_used_at
        FROM workspaces w
        LEFT JOIN resources r ON r.workspace_id = w.id
        GROUP BY w.id
        ORDER BY w.updated_at DESC, w.id
        """;

    public Task<IReadOnlyList<WorkspaceSummary>> ListAsync(CancellationToken cancellationToken = default) =>
        database.RunAsync<IReadOnlyList<WorkspaceSummary>>(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = SelectSummaries;

                using var reader = command.ExecuteReader();
                var summaries = new List<WorkspaceSummary>();
                while (reader.Read())
                {
                    summaries.Add(new WorkspaceSummary(
                        ReadGuid(reader, 0),
                        reader.GetString(1),
                        reader.GetString(2),
                        reader.GetInt32(3),
                        ReadDate(reader, 4),
                        ReadNullableDate(reader, 5)));
                }

                return summaries;
            },
            cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "SELECT COUNT(*) FROM workspaces";
                return Convert.ToInt32(command.ExecuteScalar(), CultureInfo.InvariantCulture);
            },
            cancellationToken);

    public Task<Workspace?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                var resources = ReadResources(connection, id);

                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT name, description, notes, created_at, updated_at, last_used_at
                    FROM workspaces WHERE id = $id
                    """;
                command.Parameters.AddWithValue("$id", ToText(id));

                using var reader = command.ExecuteReader();
                return reader.Read()
                    ? Workspace.Restore(
                        id,
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.GetString(2),
                        ReadDate(reader, 3),
                        ReadDate(reader, 4),
                        ReadNullableDate(reader, 5),
                        resources)
                    : null;
            },
            cancellationToken);

    public Task AddAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction();

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        INSERT INTO workspaces (id, name, description, notes, created_at, updated_at, last_used_at)
                        VALUES ($id, $name, $description, $notes, $created_at, $updated_at, $last_used_at)
                        """;
                    command.Parameters.AddWithValue("$created_at", ToText(workspace.CreatedAt));
                    AddWorkspaceValues(command, workspace);
                    command.ExecuteNonQuery();
                }

                InsertResources(connection, transaction, workspace);
                transaction.Commit();
                return true;
            },
            cancellationToken);

    public Task<bool> UpdateAsync(Workspace workspace, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction();

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = """
                        UPDATE workspaces
                        SET name = $name, description = $description, notes = $notes,
                            updated_at = $updated_at, last_used_at = $last_used_at
                        WHERE id = $id
                        """;
                    AddWorkspaceValues(command, workspace);
                    if (command.ExecuteNonQuery() == 0)
                        return false;
                }

                using (var command = connection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = "DELETE FROM resources WHERE workspace_id = $id";
                    command.Parameters.AddWithValue("$id", ToText(workspace.Id));
                    command.ExecuteNonQuery();
                }

                InsertResources(connection, transaction, workspace);
                transaction.Commit();
                return true;
            },
            cancellationToken);

    public Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = "DELETE FROM workspaces WHERE id = $id";
                command.Parameters.AddWithValue("$id", ToText(id));
                return command.ExecuteNonQuery() > 0;
            },
            cancellationToken);

    private static List<Resource> ReadResources(SqliteConnection connection, Guid workspaceId)
    {
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT id, kind, display_name, target, sort_order, requires_review, created_at, updated_at
            FROM resources WHERE workspace_id = $id ORDER BY sort_order
            """;
        command.Parameters.AddWithValue("$id", ToText(workspaceId));

        using var reader = command.ExecuteReader();
        var resources = new List<Resource>();
        while (reader.Read())
        {
            resources.Add(new Resource(
                ReadGuid(reader, 0),
                ResourceKind.Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetBoolean(5),
                ReadDate(reader, 6),
                ReadDate(reader, 7)));
        }

        return resources;
    }

    private static void InsertResources(SqliteConnection connection, SqliteTransaction transaction, Workspace workspace)
    {
        foreach (var resource in workspace.Resources)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = """
                INSERT INTO resources (id, workspace_id, kind, display_name, target, sort_order, requires_review, created_at, updated_at)
                VALUES ($id, $workspace_id, $kind, $display_name, $target, $sort_order, $requires_review, $created_at, $updated_at)
                """;
            command.Parameters.AddWithValue("$id", ToText(resource.Id));
            command.Parameters.AddWithValue("$workspace_id", ToText(workspace.Id));
            command.Parameters.AddWithValue("$kind", resource.Kind.Value);
            command.Parameters.AddWithValue("$display_name", resource.DisplayName);
            command.Parameters.AddWithValue("$target", resource.Target);
            command.Parameters.AddWithValue("$sort_order", resource.SortOrder);
            command.Parameters.AddWithValue("$requires_review", resource.RequiresReview ? 1 : 0);
            command.Parameters.AddWithValue("$created_at", ToText(resource.CreatedAt));
            command.Parameters.AddWithValue("$updated_at", ToText(resource.UpdatedAt));
            command.ExecuteNonQuery();
        }
    }

    private static void AddWorkspaceValues(SqliteCommand command, Workspace workspace)
    {
        command.Parameters.AddWithValue("$id", ToText(workspace.Id));
        command.Parameters.AddWithValue("$name", workspace.Name);
        command.Parameters.AddWithValue("$description", workspace.Description);
        command.Parameters.AddWithValue("$notes", workspace.Notes);
        command.Parameters.AddWithValue("$updated_at", ToText(workspace.UpdatedAt));
        command.Parameters.AddWithValue(
            "$last_used_at",
            workspace.LastUsedAt is { } lastUsedAt ? ToText(lastUsedAt) : DBNull.Value);
    }

    private static string ToText(Guid value) => value.ToString("D");

    private static string ToText(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static Guid ReadGuid(SqliteDataReader reader, int ordinal) => Guid.Parse(reader.GetString(ordinal));

    private static DateTimeOffset ReadDate(SqliteDataReader reader, int ordinal) =>
        DateTimeOffset.Parse(reader.GetString(ordinal), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static DateTimeOffset? ReadNullableDate(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : ReadDate(reader, ordinal);
}
