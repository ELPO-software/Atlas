using Atlas.Core.History;
using Atlas.Core.Validation;

namespace Atlas.Core.Versions;

/// <summary>A saved state of a workspace: its details, notes and resources at one moment.</summary>
/// <param name="IsAutomatic">True for the version Atlas saves by itself before going back to another one.</param>
/// <param name="Content">The workspace in the file format of <see cref="Exchange.WorkspaceDocument"/>.</param>
public sealed record WorkspaceVersion(
    Guid Id,
    Guid WorkspaceId,
    string Name,
    DateTimeOffset SavedAt,
    bool IsAutomatic,
    int ResourceCount,
    string Content);

public sealed record VersionSummary(Guid Id, Guid WorkspaceId, string Name, DateTimeOffset SavedAt, bool IsAutomatic, int ResourceCount);

/// <summary>Stores saved versions. They are deleted with their workspace, which the deletion confirmation says.</summary>
public interface IVersionRepository
{
    /// <summary>Returns the most recent versions first, without their content.</summary>
    Task<IReadOnlyList<VersionSummary>> ListAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<int> CountAsync(Guid workspaceId, CancellationToken cancellationToken = default);

    Task<WorkspaceVersion?> GetAsync(Guid versionId, CancellationToken cancellationToken = default);

    Task AddAsync(WorkspaceVersion version, HistoryEntry record, CancellationToken cancellationToken = default);
}

public static class VersionRules
{
    public const int NameMaxLength = 120;

    public static IReadOnlyList<ValidationError> ValidateName(string? name)
    {
        var errors = new List<ValidationError>();
        TextRules.CheckSingleLine(name, NameMaxLength, required: true, ValidationField.VersionName, errors);
        return errors;
    }
}

public sealed class VersionNotFoundException(Guid versionId) : Exception($"Version {versionId} does not exist.")
{
    public Guid VersionId { get; } = versionId;
}

/// <summary>A stored version that can no longer be read. It is left as it is.</summary>
public sealed class UnreadableVersionException(Guid versionId) : Exception($"Version {versionId} cannot be read.")
{
    public Guid VersionId { get; } = versionId;
}
