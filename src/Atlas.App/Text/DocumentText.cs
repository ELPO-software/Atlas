using Atlas.App.Resources;
using Atlas.Core.Exchange;

namespace Atlas.App.Text;

/// <summary>Explains why a file cannot be imported, and what will happen to each resource it holds.</summary>
internal static class DocumentText
{
    public static string Describe(DocumentError error) => error.Problem switch
    {
        DocumentProblem.FileNotFound => Strings.FileProblemFileNotFound,
        DocumentProblem.Unreadable => Strings.FileProblemUnreadable,
        DocumentProblem.TooLarge => Strings.FileProblemTooLarge,
        DocumentProblem.NotText => Strings.FileProblemNotText,
        DocumentProblem.InvalidJson => error.Location.Length == 0
            ? Strings.FileProblemInvalidJson
            : UiText.Format(Strings.FileProblemInvalidJsonAtLine, error.Location),
        DocumentProblem.DuplicateProperty => UiText.Format(Strings.FileProblemDuplicateProperty, error.Location),
        DocumentProblem.NotAnAtlasFile => Strings.FileProblemNotAnAtlasFile,
        DocumentProblem.NewerVersion => Strings.FileProblemNewerVersion,
        DocumentProblem.UnsupportedVersion => Strings.FileProblemUnsupportedVersion,
        DocumentProblem.UnexpectedProperty => UiText.Format(Strings.FileProblemUnexpectedProperty, error.Location),
        DocumentProblem.MissingProperty => UiText.Format(Strings.FileProblemMissingProperty, error.Location),
        DocumentProblem.WrongType => UiText.Format(Strings.FileProblemWrongType, error.Location),
        _ => Strings.FileProblemTooManyResources,
    };

    public static string? Note(ImportedResource resource) => resource.Note switch
    {
        ImportNote.NeedsApproval => Strings.ImportNoteNeedsApproval,
        ImportNote.UnknownKind => Strings.ImportNoteUnknownKind,
        ImportNote.Refused => UiText.Format(
            Strings.ImportNoteRefused,
            resource.Refusal is { } refusal ? ValidationText.Describe(refusal) : Strings.ImportRefusedUnreadableKind),
        _ => null,
    };
}
