using Atlas.Core.Workspaces;

namespace Atlas.Core.Tests;

public sealed class WorkspaceSummaryTests
{
    private static readonly DateTimeOffset Earlier = new(2026, 11, 1, 10, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 11, 5, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void LastActivityAt_IsTheModificationDateForAWorkspaceNeverRestored() =>
        Assert.Equal(Earlier, Summary(updatedAt: Earlier, lastUsedAt: null).LastActivityAt);

    [Fact]
    public void LastActivityAt_IsTheRestorationWhenItIsMoreRecent() =>
        Assert.Equal(Later, Summary(updatedAt: Earlier, lastUsedAt: Later).LastActivityAt);

    [Fact]
    public void LastActivityAt_IsTheModificationWhenItIsMoreRecent() =>
        Assert.Equal(Later, Summary(updatedAt: Later, lastUsedAt: Earlier).LastActivityAt);

    private static WorkspaceSummary Summary(DateTimeOffset updatedAt, DateTimeOffset? lastUsedAt) =>
        new(Guid.NewGuid(), "Nom", string.Empty, 0, updatedAt, lastUsedAt);
}
