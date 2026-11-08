using Atlas.Core.Resources;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;

namespace Atlas.Core.Exchange;

public enum ImportNote
{
    /// <summary>Imported as it is.</summary>
    None,

    /// <summary>Imported, but Atlas will not open it until the user approves it.</summary>
    NeedsApproval,

    /// <summary>Imported and kept, but Atlas cannot open this type.</summary>
    UnknownKind,

    /// <summary>Not imported, because it breaks a rule.</summary>
    Refused,
}

/// <param name="Refusal">The rule a refused resource breaks, or null when its type is not even a valid name.</param>
public sealed record ImportedResource(DocumentResource Source, ResourceKind? Kind, ImportNote Note, ValidationError? Refusal);

/// <summary>
/// What importing a file will create, decided from the file alone. Nothing it points to is looked up: a network path
/// is not contacted, and nothing is started. Applications and network locations wait for the user's approval.
/// </summary>
public sealed class ImportPlan
{
    private ImportPlan(DocumentContent content, IReadOnlyList<ValidationError> workspaceErrors, IReadOnlyList<ImportedResource> resources)
    {
        Content = content;
        WorkspaceErrors = workspaceErrors;
        Resources = resources;
    }

    public DocumentContent Content { get; }

    /// <summary>Problems with the name, description or notes. Any of them prevents the import.</summary>
    public IReadOnlyList<ValidationError> WorkspaceErrors { get; }

    public IReadOnlyList<ImportedResource> Resources { get; }

    public bool CanImport => WorkspaceErrors.Count == 0;

    public static ImportPlan From(DocumentContent content)
    {
        var errors = new List<ValidationError>();
        errors.AddRange(WorkspaceRules.ValidateDetails(content.Name, content.Description));
        errors.AddRange(WorkspaceRules.ValidateNotes(content.Notes));

        return new ImportPlan(content, errors, content.Resources.Select(Classify).ToList());
    }

    private static ImportedResource Classify(DocumentResource source)
    {
        // The file's own approval flag is never trusted: approval is decided here.
        if (!ResourceKind.TryParse(source.Kind, out var kind))
            return new ImportedResource(source, null, ImportNote.Refused, null);

        var refusal = ResourceRules.Validate(kind, source.Name, source.Target).FirstOrDefault();
        if (refusal is not null)
            return new ImportedResource(source, kind, ImportNote.Refused, refusal);

        var known = kind == ResourceKind.Application || kind == ResourceKind.File || kind == ResourceKind.Folder || kind == ResourceKind.Url;
        if (!known)
            return new ImportedResource(source, kind, ImportNote.UnknownKind, null);

        var needsApproval = kind == ResourceKind.Application || ResourceTargets.IsNetworkPath(source.Target.Trim());
        return new ImportedResource(source, kind, needsApproval ? ImportNote.NeedsApproval : ImportNote.None, null);
    }
}
