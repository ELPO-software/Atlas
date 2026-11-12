using Atlas.Core.Resources;
using Atlas.Core.Workspaces;

namespace Atlas.Core.Detection;

/// <param name="Suggestions">The environments to show the user, most recently seen first.</param>
/// <param name="ToNotify">The one environment worth a notification now, if any.</param>
public sealed record DetectionScan(DateTimeOffset At, IReadOnlyList<StoredProposal> Suggestions, StoredProposal? ToNotify);

/// <summary>
/// Observes the session, keeps what it proposes, and applies the decisions of the user. Nothing is ever saved as a
/// workspace without the user asking for it, and an environment the user ignored or saved is not proposed again.
/// </summary>
public sealed class EnvironmentDetectionService(
    IWorkObserver observer,
    IDetectionStore store,
    WorkspaceService workspaces,
    TimeProvider timeProvider)
{
    /// <summary>A project must still be there at a later observation before Atlas notifies about it.</summary>
    public static readonly TimeSpan StableAfter = TimeSpan.FromSeconds(90);

    /// <summary>At most one notification in this time, whatever happens.</summary>
    public static readonly TimeSpan NotificationInterval = TimeSpan.FromMinutes(20);

    /// <summary>How long "later" lasts.</summary>
    public static readonly TimeSpan SnoozeDuration = TimeSpan.FromHours(8);

    /// <summary>An environment no longer seen for this long is forgotten, unless the user decided about it.</summary>
    public static readonly TimeSpan ForgetAfter = TimeSpan.FromDays(30);

    /// <summary>Two contexts sharing this share of their resources are the same context.</summary>
    public const double SameContextSimilarity = 0.6;

    /// <summary>A context becomes a suggestion once seen on this many different days.</summary>
    public const int RecurringContextDays = 2;

    /// <summary>A context whose resources are mostly saved already is not proposed.</summary>
    public const double CoveredShare = 0.8;

    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<DetectionScan> ScanAsync(CancellationToken cancellationToken = default)
    {
        var observation = await observer.ObserveAsync(cancellationToken);
        var proposals = EnvironmentDetector.Propose(observation);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var now = timeProvider.GetUtcNow();
            var today = DateOnly.FromDateTime(timeProvider.GetLocalNow().DateTime);
            var stored = (await store.ListAsync(cancellationToken)).ToDictionary(p => p.Key, StringComparer.Ordinal);
            var saved = await SavedIdentitiesAsync(cancellationToken);

            foreach (var proposal in proposals)
            {
                var existing = Match(proposal, stored.Values);
                StoredProposal next;

                if (existing is null)
                {
                    // Something the user already keeps in a workspace is not news.
                    if (IsCovered(proposal, saved))
                        continue;

                    next = new StoredProposal(proposal, ProposalStatus.Pending, now, now, 1, today, null, null, null);
                }
                else
                {
                    var seenDays = existing.LastSeenDay == today ? existing.SeenDays : existing.SeenDays + 1;
                    next = existing with { LastSeen = now, LastSeenDay = today, SeenDays = seenDays };

                    // The content follows what is open now; the key stays, so that decisions keep applying.
                    if (existing.Status is ProposalStatus.Pending or ProposalStatus.Snoozed)
                        next = next with { Proposal = proposal with { Key = existing.Key } };
                }

                if (next.Status == ProposalStatus.Snoozed && next.SnoozedUntil <= now)
                    next = next with { Status = ProposalStatus.Pending, SnoozedUntil = null, NotifiedAt = null };

                await store.SaveAsync(next, cancellationToken);
                stored[next.Key] = next;
            }

            var forgotten = stored.Values
                .Where(p => p.Status is ProposalStatus.Pending or ProposalStatus.Snoozed && now - p.LastSeen > ForgetAfter)
                .Select(p => p.Key)
                .ToList();
            if (forgotten.Count > 0)
            {
                await store.DeleteAsync(forgotten, cancellationToken);
                forgotten.ForEach(key => stored.Remove(key));
            }

            var suggestions = stored.Values
                .Where(p => IsSuggestion(p, saved))
                .OrderByDescending(p => p.LastSeen)
                .ThenBy(p => p.Proposal.Title, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            return new DetectionScan(now, suggestions, ChooseNotification(suggestions, stored.Values, now));
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The environments to show, without observing the session again.</summary>
    public async Task<IReadOnlyList<StoredProposal>> ListSuggestionsAsync(CancellationToken cancellationToken = default)
    {
        var saved = await SavedIdentitiesAsync(cancellationToken);
        return (await store.ListAsync(cancellationToken))
            .Where(p => IsSuggestion(p, saved))
            .OrderByDescending(p => p.LastSeen)
            .ToList();
    }

    public async Task<IReadOnlyList<StoredProposal>> ListIgnoredAsync(CancellationToken cancellationToken = default) =>
        (await store.ListAsync(cancellationToken))
            .Where(p => p.Status == ProposalStatus.Ignored)
            .OrderBy(p => p.Proposal.Title, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

    /// <summary>Saves the chosen resources of a proposal as a new workspace.</summary>
    /// <param name="name">The name the user gave the workspace.</param>
    /// <param name="resources">The resources the user kept, which must belong to the proposal.</param>
    public async Task<Workspace> SaveAsync(
        string key, string? name, IReadOnlyList<ProposedResource> resources, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(resources);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            var proposal = await GetAsync(key, cancellationToken);
            var offered = proposal.Proposal.Resources.Select(EnvironmentDetector.Identity).ToHashSet(StringComparer.Ordinal);
            if (resources.Any(r => !offered.Contains(EnvironmentDetector.Identity(r))))
                throw new ArgumentException("Only resources of the proposal can be saved.", nameof(resources));

            var workspace = await workspaces.CreateFromDetectionAsync(
                name, resources.Select(r => (r.Kind, r.DisplayName, r.Target)).ToList(), cancellationToken);

            await store.SaveAsync(
                proposal with { Status = ProposalStatus.Saved, WorkspaceId = workspace.Id, SnoozedUntil = null },
                cancellationToken);
            return workspace;
        }
        finally
        {
            _gate.Release();
        }
    }

    public Task IgnoreAsync(string key, CancellationToken cancellationToken = default) =>
        ChangeAsync(key, p => p with { Status = ProposalStatus.Ignored, SnoozedUntil = null }, cancellationToken);

    public Task SnoozeAsync(string key, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            key,
            p => p with { Status = ProposalStatus.Snoozed, SnoozedUntil = timeProvider.GetUtcNow() + SnoozeDuration },
            cancellationToken);

    /// <summary>Takes back an "ignore": the environment becomes a suggestion again, without a new notification.</summary>
    public Task RestoreIgnoredAsync(string key, CancellationToken cancellationToken = default) =>
        ChangeAsync(
            key,
            p => p.Status == ProposalStatus.Ignored ? p with { Status = ProposalStatus.Pending, NotifiedAt = timeProvider.GetUtcNow() } : p,
            cancellationToken);

    public Task MarkNotifiedAsync(string key, CancellationToken cancellationToken = default) =>
        ChangeAsync(key, p => p with { NotifiedAt = timeProvider.GetUtcNow() }, cancellationToken);

    /// <summary>Forgets every environment Atlas observed, except the record of those the user saved.</summary>
    public async Task ForgetObservationsAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var keys = (await store.ListAsync(cancellationToken))
                .Where(p => p.Status != ProposalStatus.Saved)
                .Select(p => p.Key)
                .ToList();
            if (keys.Count > 0)
                await store.DeleteAsync(keys, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task ChangeAsync(string key, Func<StoredProposal, StoredProposal> change, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await store.SaveAsync(change(await GetAsync(key, cancellationToken)), cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<StoredProposal> GetAsync(string key, CancellationToken cancellationToken) =>
        (await store.ListAsync(cancellationToken)).FirstOrDefault(p => p.Key == key)
        ?? throw new KeyNotFoundException($"No proposed environment has the key '{key}'.");

    private async Task<HashSet<string>> SavedIdentitiesAsync(CancellationToken cancellationToken)
    {
        var targets = await workspaces.ListResourceTargetsAsync(cancellationToken);
        return targets
            .Select(t => EnvironmentDetector.Identity(new ProposedResource(t.Kind, string.Empty, t.Target, ProposalReason.RunningApplication)))
            .ToHashSet(StringComparer.Ordinal);
    }

    private static StoredProposal? Match(EnvironmentProposal proposal, IEnumerable<StoredProposal> stored)
    {
        if (proposal.Kind == ProposalKind.Project)
            return stored.FirstOrDefault(p => p.Key == proposal.Key);

        return stored
            .Where(p => p.Proposal.Kind == ProposalKind.RecurringContext)
            .Select(p => (Proposal: p, Similarity: EnvironmentDetector.Similarity(p.Proposal.Resources, proposal.Resources)))
            .Where(m => m.Similarity >= SameContextSimilarity)
            .OrderByDescending(m => m.Similarity)
            .Select(m => m.Proposal)
            .FirstOrDefault();
    }

    private static bool IsCovered(EnvironmentProposal proposal, HashSet<string> saved)
    {
        if (proposal.Kind == ProposalKind.Project)
        {
            var folder = proposal.Resources.FirstOrDefault(r => r.Reason == ProposalReason.ProjectFolder);
            return folder is not null && saved.Contains(EnvironmentDetector.Identity(folder));
        }

        var known = proposal.Resources.Count(r => saved.Contains(EnvironmentDetector.Identity(r)));
        return known >= proposal.Resources.Count * CoveredShare;
    }

    private static bool IsSuggestion(StoredProposal proposal, HashSet<string> saved) =>
        proposal.Status is ProposalStatus.Pending or ProposalStatus.Snoozed
        && (proposal.Proposal.Kind == ProposalKind.Project || proposal.SeenDays >= RecurringContextDays)
        && !IsCovered(proposal.Proposal, saved);

    private static StoredProposal? ChooseNotification(
        IReadOnlyList<StoredProposal> suggestions, IEnumerable<StoredProposal> all, DateTimeOffset now)
    {
        var lastNotification = all.Max(p => p.NotifiedAt);
        if (lastNotification is { } last && now - last < NotificationInterval)
            return null;

        // Only what is on screen right now, never notified, and still there since an earlier observation.
        return suggestions.FirstOrDefault(p =>
            p.Status == ProposalStatus.Pending
            && p.NotifiedAt is null
            && p.LastSeen == now
            && (p.Proposal.Kind == ProposalKind.RecurringContext || now - p.FirstSeen >= StableAfter));
    }
}
