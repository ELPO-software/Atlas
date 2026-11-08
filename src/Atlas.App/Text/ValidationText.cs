using Atlas.App.Resources;
using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;

namespace Atlas.App.Text;

/// <summary>Turns the codes reported by the business rules into messages for the user.</summary>
internal static class ValidationText
{
    public static string Describe(ValidationError error) => (error.Field, error.Code) switch
    {
        (ValidationField.WorkspaceName, ValidationCode.Required) => Strings.NameRequired,
        (ValidationField.WorkspaceName, ValidationCode.TooLong) => UiText.Format(Strings.NameTooLong, WorkspaceRules.NameMaxLength),
        (ValidationField.WorkspaceName, ValidationCode.ControlCharacters) => Strings.NameInvalidCharacters,
        (ValidationField.WorkspaceDescription, ValidationCode.TooLong) => UiText.Format(Strings.DescriptionTooLong, WorkspaceRules.DescriptionMaxLength),
        (ValidationField.WorkspaceNotes, ValidationCode.TooLong) => UiText.Format(Strings.NotesTooLong, WorkspaceRules.NotesMaxLength),
        (ValidationField.ResourceDisplayName, ValidationCode.Required) => Strings.ResourceNameRequired,
        (ValidationField.ResourceDisplayName, ValidationCode.TooLong) => UiText.Format(Strings.NameTooLong, ResourceRules.DisplayNameMaxLength),
        (ValidationField.ResourceDisplayName, ValidationCode.ControlCharacters) => Strings.NameInvalidCharacters,
        (ValidationField.ResourceTarget, ValidationCode.Required) => Strings.TargetRequired,
        (ValidationField.ResourceTarget, ValidationCode.TooLong) => Strings.TargetTooLong,
        (ValidationField.ResourceTarget, ValidationCode.ControlCharacters) => Strings.TargetInvalidCharacters,
        (ValidationField.ResourceTarget, ValidationCode.InvalidPath) => Strings.TargetInvalidPath,
        (ValidationField.ResourceTarget, ValidationCode.UnsupportedLink) => Strings.TargetUnsupportedLink,
        (ValidationField.ResourceTarget, ValidationCode.ExecutableFile) => Strings.TargetExecutableFile,
        (ValidationField.ResourceTarget, ValidationCode.NotAnApplication) => Strings.TargetNotAnApplication,
        _ => Strings.ValueRefused,
    };

    public static string? FirstFor(AtlasValidationException exception, ValidationField field) =>
        exception.Errors.FirstOrDefault(e => e.Field == field) is { } error ? Describe(error) : null;
}
