namespace Atlas.Core.Validation;

/// <summary>Identifies the value a validation error is about. The interface turns it into a message.</summary>
public enum ValidationField
{
    WorkspaceName,
    WorkspaceDescription,
    WorkspaceNotes,
    ResourceDisplayName,
    ResourceTarget,
}

public enum ValidationCode
{
    Required,
    TooLong,
    ControlCharacters,

    /// <summary>Not a complete path on this computer or the network, or one Windows would read differently.</summary>
    InvalidPath,

    /// <summary>A link that is not a web address starting with http or https.</summary>
    UnsupportedLink,

    /// <summary>A file that would run code when opened. Programs are added as applications.</summary>
    ExecutableFile,

    /// <summary>Neither a program (.exe) nor an application installed on this computer.</summary>
    NotAnApplication,
}

public sealed record ValidationError(ValidationField Field, ValidationCode Code);
