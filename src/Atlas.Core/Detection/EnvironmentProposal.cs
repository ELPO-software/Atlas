using Atlas.Core.Resources;

namespace Atlas.Core.Detection;

public enum ProposalKind
{
    /// <summary>A project folder and what works on it.</summary>
    Project,

    /// <summary>Applications, folders and files that come back together, on several days.</summary>
    RecurringContext,
}

/// <summary>How sure Atlas is. A probable environment is never presented as a certain one.</summary>
public enum DetectionConfidence
{
    /// <summary>Seen directly: a program has the project open.</summary>
    Detected,

    /// <summary>Inferred: only a folder window points to it, or it is a combination seen several times.</summary>
    Probable,
}

/// <summary>Why a resource is part of a proposal, so the user can judge it.</summary>
public enum ProposalReason
{
    ProjectFolder,
    ProjectEntryFile,
    Repository,
    OpenInApplication,
    OpenInExplorer,
    RunningApplication,
}

public sealed record ProposedResource(ResourceKind Kind, string DisplayName, string Target, ProposalReason Reason);

/// <param name="Key">Identifies the environment from one observation to the next.</param>
/// <param name="Title">The name of the project, or of the main applications of a context. It is data, not interface text.</param>
public sealed record EnvironmentProposal(
    string Key,
    ProposalKind Kind,
    string Title,
    DetectionConfidence Confidence,
    IReadOnlyList<ProposedResource> Resources);
