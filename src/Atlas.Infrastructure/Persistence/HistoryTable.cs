using System.Globalization;
using Atlas.Core.History;
using Microsoft.Data.Sqlite;

namespace Atlas.Infrastructure.Persistence;

/// <summary>Writes history lines inside the transaction of the change they describe.</summary>
internal static class HistoryTable
{
    /// <summary>
    /// Notes are saved a second after typing stops. Saves of the same notes within this time share one line, so the
    /// history keeps showing what the user did rather than how often the notes were saved.
    /// </summary>
    internal static readonly TimeSpan NotesMergeWindow = TimeSpan.FromMinutes(30);

    private static readonly Dictionary<HistoryAction, string> Names = new()
    {
        [HistoryAction.WorkspaceCreated] = "workspace-created",
        [HistoryAction.WorkspaceDuplicated] = "workspace-duplicated",
        [HistoryAction.WorkspaceImported] = "workspace-imported",
        [HistoryAction.WorkspaceEdited] = "workspace-edited",
        [HistoryAction.NotesEdited] = "notes-edited",
        [HistoryAction.ResourceAdded] = "resource-added",
        [HistoryAction.ResourceEdited] = "resource-edited",
        [HistoryAction.ResourceRemoved] = "resource-removed",
        [HistoryAction.ResourceApproved] = "resource-approved",
        [HistoryAction.WorkspaceDeleted] = "workspace-deleted",
        [HistoryAction.VersionSaved] = "version-saved",
        [HistoryAction.VersionRestored] = "version-restored",
        [HistoryAction.WorkspaceRestored] = "workspace-restored",
        [HistoryAction.WorkspaceExported] = "workspace-exported",
        [HistoryAction.WorkspaceDetected] = "workspace-detected",
    };

    private static readonly Dictionary<string, HistoryAction> Actions = Names.ToDictionary(pair => pair.Value, pair => pair.Key);

    public static string NameOf(HistoryAction action) => Names[action];

    /// <returns>False for an action written by a newer version of Atlas.</returns>
    public static bool TryParse(string name, out HistoryAction action) => Actions.TryGetValue(name, out action);

    public static void Write(SqliteConnection connection, SqliteTransaction transaction, HistoryEntry entry)
    {
        if (entry.Action == HistoryAction.NotesEdited && TryMergeNotes(connection, transaction, entry))
            return;

        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO history (occurred_at, action, workspace_id, workspace_name, detail, restored, missing, failed, skipped)
            VALUES ($occurred_at, $action, $workspace_id, $workspace_name, $detail, $restored, $missing, $failed, $skipped)
            """;
        command.Parameters.AddWithValue("$occurred_at", Text(entry.OccurredAt));
        command.Parameters.AddWithValue("$action", NameOf(entry.Action));
        command.Parameters.AddWithValue("$workspace_id", entry.WorkspaceId.ToString("D"));
        command.Parameters.AddWithValue("$workspace_name", entry.WorkspaceName);
        command.Parameters.AddWithValue("$detail", entry.Detail);
        command.Parameters.AddWithValue("$restored", (object?)entry.Counts?.Restored ?? DBNull.Value);
        command.Parameters.AddWithValue("$missing", (object?)entry.Counts?.Missing ?? DBNull.Value);
        command.Parameters.AddWithValue("$failed", (object?)entry.Counts?.Failed ?? DBNull.Value);
        command.Parameters.AddWithValue("$skipped", (object?)entry.Counts?.Skipped ?? DBNull.Value);
        command.ExecuteNonQuery();
    }

    public static string Text(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    /// <summary>Moves the latest line forward when it is about the same notes and recent enough.</summary>
    private static bool TryMergeNotes(SqliteConnection connection, SqliteTransaction transaction, HistoryEntry entry)
    {
        using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE history SET occurred_at = $occurred_at, workspace_name = $workspace_name
            WHERE id = (SELECT MAX(id) FROM history)
              AND action = $action AND workspace_id = $workspace_id AND occurred_at >= $since
            """;
        command.Parameters.AddWithValue("$occurred_at", Text(entry.OccurredAt));
        command.Parameters.AddWithValue("$workspace_name", entry.WorkspaceName);
        command.Parameters.AddWithValue("$action", NameOf(HistoryAction.NotesEdited));
        command.Parameters.AddWithValue("$workspace_id", entry.WorkspaceId.ToString("D"));
        command.Parameters.AddWithValue("$since", Text(entry.OccurredAt - NotesMergeWindow));
        return command.ExecuteNonQuery() > 0;
    }
}
