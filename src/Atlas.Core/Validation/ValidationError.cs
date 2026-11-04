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
}

public sealed record ValidationError(ValidationField Field, ValidationCode Code);
