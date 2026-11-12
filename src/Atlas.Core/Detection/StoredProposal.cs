namespace Atlas.Core.Detection;

public enum ProposalStatus
{
    /// <summary>Waiting for the user to decide.</summary>
    Pending,

    /// <summary>The user chose to decide later. It stays among the suggestions, without notifications until then.</summary>
    Snoozed,

    /// <summary>The user does not want it. Atlas never proposes it again, unless the user takes the decision back.</summary>
    Ignored,

    /// <summary>The user saved it as a workspace.</summary>
    Saved,
}

/// <summary>What Atlas remembers about a proposed environment, on this computer only.</summary>
/// <param name="SeenDays">On how many different days the environment was observed.</param>
/// <param name="LastSeenDay">The local date of the last observation, to count days.</param>
/// <param name="NotifiedAt">When Atlas last notified the user about it, if it did.</param>
/// <param name="WorkspaceId">The workspace created from it, once saved.</param>
public sealed record StoredProposal(
    EnvironmentProposal Proposal,
    ProposalStatus Status,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastSeen,
    int SeenDays,
    DateOnly LastSeenDay,
    DateTimeOffset? SnoozedUntil,
    DateTimeOffset? NotifiedAt,
    Guid? WorkspaceId)
{
    public string Key => Proposal.Key;
}

/// <summary>Keeps the proposed environments and the decisions of the user about them.</summary>
public interface IDetectionStore
{
    Task<IReadOnlyList<StoredProposal>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Adds the proposal, or replaces the one with the same key.</summary>
    Task SaveAsync(StoredProposal proposal, CancellationToken cancellationToken = default);

    Task DeleteAsync(IReadOnlyCollection<string> keys, CancellationToken cancellationToken = default);
}
