using System.Globalization;
using Atlas.Core.History;

namespace Atlas.Infrastructure.Persistence;

public sealed class SqliteHistoryRepository(SqliteDatabase database) : IHistoryRepository
{
    public Task<IReadOnlyList<HistoryEntry>> ListAsync(int limit, CancellationToken cancellationToken = default) =>
        database.RunAsync<IReadOnlyList<HistoryEntry>>(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT occurred_at, action, workspace_id, workspace_name, detail, restored, missing, failed, skipped
                    FROM history ORDER BY occurred_at DESC, id DESC LIMIT $limit
                    """;
                command.Parameters.AddWithValue("$limit", limit);

                using var reader = command.ExecuteReader();
                var entries = new List<HistoryEntry>();
                while (reader.Read())
                {
                    // A line written by a newer version stays in the file; this version just does not show it.
                    if (!HistoryTable.TryParse(reader.GetString(1), out var action))
                        continue;

                    var counts = reader.IsDBNull(5)
                        ? null
                        : new RestoreCounts(reader.GetInt32(5), reader.GetInt32(6), reader.GetInt32(7), reader.GetInt32(8));

                    entries.Add(new HistoryEntry(
                        DateTimeOffset.Parse(reader.GetString(0), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                        action,
                        Guid.Parse(reader.GetString(2)),
                        reader.GetString(3),
                        reader.GetString(4),
                        counts));
                }

                return entries;
            },
            cancellationToken);

    public Task RecordAsync(HistoryEntry entry, CancellationToken cancellationToken = default) =>
        database.RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction();
                HistoryTable.Write(connection, transaction, entry);
                transaction.Commit();
                return true;
            },
            cancellationToken);
}
