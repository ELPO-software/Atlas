namespace Atlas.Core.Resources;

/// <param name="Target">The path or address the resource points to. Only surrounding spaces are removed.</param>
/// <param name="RequiresReview">True when the user must approve the resource before Atlas may open it.</param>
public sealed record Resource(
    Guid Id,
    ResourceKind Kind,
    string DisplayName,
    string Target,
    int SortOrder,
    bool RequiresReview,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
