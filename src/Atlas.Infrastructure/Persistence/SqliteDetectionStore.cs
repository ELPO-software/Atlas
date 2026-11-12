using System.Globalization;
using System.Text.Json;
using Atlas.Core.Detection;
using Atlas.Core.Resources;
using Microsoft.Data.Sqlite;

namespace Atlas.Infrastructure.Persistence;

/// <summary>
/// Stores proposed environments. Values are written as text names, and a row holding a value this version does not
/// know, written by a newer one, stays in the file without being shown.
/// </summary>
public sealed class SqliteDetectionStore(SqliteDatabase database) : IDetectionStore
{
    private static readonly Dictionary<ProposalKind, string> Kinds = new()
    {
        [ProposalKind.Project] = "project",
        [ProposalKind.RecurringContext] = "recurring-context",
    };

    private static readonly Dictionary<DetectionConfidence, string> Confidences = new()
    {
        [DetectionConfidence.Detected] = "detected",
        [DetectionConfidence.Probable] = "probable",
    };

    private static readonly Dictionary<ProposalStatus, string> Statuses = new()
    {
        [ProposalStatus.Pending] = "pending",
        [ProposalStatus.Snoozed] = "snoozed",
        [ProposalStatus.Ignored] = "ignored",
        [ProposalStatus.Saved] = "saved",
    };

    private static readonly Dictionary<ProposalReason, string> Reasons = new()
    {
        [ProposalReason.ProjectFolder] = "project-folder",
        [ProposalReason.ProjectEntryFile] = "project-entry-file",
        [ProposalReason.Repository] = "repository",
        [ProposalReason.OpenInApplication] = "open-in-application",
        [ProposalReason.OpenInExplorer] = "open-in-explorer",
        [ProposalReason.RunningApplication] = "running-application",
    };

    public Task<IReadOnlyList<StoredProposal>> ListAsync(CancellationToken cancellationToken = default) =>
        database.RunAsync<IReadOnlyList<StoredProposal>>(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    SELECT key, kind, title, confidence, resources, status, first_seen, last_seen, seen_days,
                           last_seen_day, snoozed_until, notified_at, workspace_id
                    FROM detected_environments
                    """;

                using var reader = command.ExecuteReader();
                var proposals = new List<StoredProposal>();
                while (reader.Read())
                {
                    if (TryRead(reader) is { } proposal)
                        proposals.Add(proposal);
                }

                return proposals;
            },
            cancellationToken);

    public Task SaveAsync(StoredProposal proposal, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);

        return database.RunAsync(
            connection =>
            {
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO detected_environments (key, kind, title, confidence, resources, status, first_seen, last_seen,
                                                       seen_days, last_seen_day, snoozed_until, notified_at, workspace_id)
                    VALUES ($key, $kind, $title, $confidence, $resources, $status, $first_seen, $last_seen,
                            $seen_days, $last_seen_day, $snoozed_until, $notified_at, $workspace_id)
                    ON CONFLICT (key) DO UPDATE SET
                        kind = excluded.kind, title = excluded.title, confidence = excluded.confidence,
                        resources = excluded.resources, status = excluded.status, first_seen = excluded.first_seen,
                        last_seen = excluded.last_seen, seen_days = excluded.seen_days, last_seen_day = excluded.last_seen_day,
                        snoozed_until = excluded.snoozed_until, notified_at = excluded.notified_at,
                        workspace_id = excluded.workspace_id
                    """;
                var p = proposal.Proposal;
                command.Parameters.AddWithValue("$key", p.Key);
                command.Parameters.AddWithValue("$kind", Kinds[p.Kind]);
                command.Parameters.AddWithValue("$title", p.Title);
                command.Parameters.AddWithValue("$confidence", Confidences[p.Confidence]);
                command.Parameters.AddWithValue("$resources", WriteResources(p.Resources));
                command.Parameters.AddWithValue("$status", Statuses[proposal.Status]);
                command.Parameters.AddWithValue("$first_seen", HistoryTable.Text(proposal.FirstSeen));
                command.Parameters.AddWithValue("$last_seen", HistoryTable.Text(proposal.LastSeen));
                command.Parameters.AddWithValue("$seen_days", proposal.SeenDays);
                command.Parameters.AddWithValue("$last_seen_day", proposal.LastSeenDay.ToString("O", CultureInfo.InvariantCulture));
                command.Parameters.AddWithValue("$snoozed_until", Nullable(proposal.SnoozedUntil));
                command.Parameters.AddWithValue("$notified_at", Nullable(proposal.NotifiedAt));
                command.Parameters.AddWithValue("$workspace_id", (object?)proposal.WorkspaceId?.ToString("D") ?? DBNull.Value);
                return command.ExecuteNonQuery();
            },
            cancellationToken);
    }

    public Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(keys);

        return database.RunAsync(
            connection =>
            {
                using var transaction = connection.BeginTransaction();
                using var command = connection.CreateCommand();
                command.Transaction = transaction;
                command.CommandText = "DELETE FROM detected_environments WHERE key = $key";
                var key = command.Parameters.Add("$key", SqliteType.Text);
                foreach (var value in keys)
                {
                    key.Value = value;
                    command.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            },
            cancellationToken);
    }

    private static StoredProposal? TryRead(SqliteDataReader reader)
    {
        if (!TryParse(Kinds, reader.GetString(1), out var kind)
            || !TryParse(Confidences, reader.GetString(3), out var confidence)
            || !TryParse(Statuses, reader.GetString(5), out var status)
            || ReadResources(reader.GetString(4)) is not { } resources)
        {
            return null;
        }

        var proposal = new EnvironmentProposal(reader.GetString(0), kind, reader.GetString(2), confidence, resources);
        return new StoredProposal(
            proposal,
            status,
            ReadDate(reader.GetString(6)),
            ReadDate(reader.GetString(7)),
            reader.GetInt32(8),
            DateOnly.ParseExact(reader.GetString(9), "O", CultureInfo.InvariantCulture),
            reader.IsDBNull(10) ? null : ReadDate(reader.GetString(10)),
            reader.IsDBNull(11) ? null : ReadDate(reader.GetString(11)),
            reader.IsDBNull(12) ? null : Guid.Parse(reader.GetString(12)));
    }

    private static string WriteResources(IReadOnlyList<ProposedResource> resources) =>
        JsonSerializer.Serialize(resources.Select(r => new StoredResource(r.Kind.Value, r.DisplayName, r.Target, Reasons[r.Reason])).ToList());

    /// <returns>Null if a resource uses a value this version does not know.</returns>
    private static List<ProposedResource>? ReadResources(string json)
    {
        List<StoredResource>? stored;
        try
        {
            stored = JsonSerializer.Deserialize<List<StoredResource>>(json);
        }
        catch (JsonException)
        {
            return null;
        }

        var resources = new List<ProposedResource>();
        foreach (var resource in stored ?? [])
        {
            if (!ResourceKind.TryParse(resource.Kind, out var resourceKind) || !TryParse(Reasons, resource.Reason ?? string.Empty, out var reason)
                || resource.Name is null || resource.Target is null)
            {
                return null;
            }

            resources.Add(new ProposedResource(resourceKind, resource.Name, resource.Target, reason));
        }

        return resources;
    }

    private static bool TryParse<T>(Dictionary<T, string> names, string value, out T result)
        where T : struct
    {
        foreach (var (key, name) in names)
        {
            if (name == value)
            {
                result = key;
                return true;
            }
        }

        result = default;
        return false;
    }

    private static object Nullable(DateTimeOffset? value) => value is { } date ? HistoryTable.Text(date) : DBNull.Value;

    private static DateTimeOffset ReadDate(string text) =>
        DateTimeOffset.Parse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record StoredResource(string Kind, string? Name, string? Target, string? Reason);
}
