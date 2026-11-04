namespace Atlas.Core.Workspaces;

/// <summary>What a list needs to show about a workspace, without loading its notes and resources.</summary>
public sealed record WorkspaceSummary(
    Guid Id,
    string Name,
    string Description,
    int ResourceCount,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? LastUsedAt);
