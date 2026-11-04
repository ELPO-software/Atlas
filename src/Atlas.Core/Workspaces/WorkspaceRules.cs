using Atlas.Core.Validation;

namespace Atlas.Core.Workspaces;

public static class WorkspaceRules
{
    public const int NameMaxLength = 120;
    public const int DescriptionMaxLength = 2_000;
    public const int NotesMaxLength = 1_000_000;

    public static IReadOnlyList<ValidationError> ValidateDetails(string? name, string? description)
    {
        var errors = new List<ValidationError>();
        TextRules.CheckSingleLine(name, NameMaxLength, required: true, ValidationField.WorkspaceName, errors);
        TextRules.CheckMultiline(description, DescriptionMaxLength, ValidationField.WorkspaceDescription, errors);
        return errors;
    }

    public static IReadOnlyList<ValidationError> ValidateNotes(string? notes)
    {
        var errors = new List<ValidationError>();
        TextRules.CheckMultiline(notes, NotesMaxLength, ValidationField.WorkspaceNotes, errors);
        return errors;
    }
}
