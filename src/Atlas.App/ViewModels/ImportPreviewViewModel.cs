using Atlas.App.Resources;
using Atlas.App.Text;
using Atlas.Core.Exchange;
using Atlas.Core.Resources;

namespace Atlas.App.ViewModels;

/// <param name="Note">What will happen to this resource, when it is not simply imported.</param>
public sealed record ImportPreviewItem(string Name, string KindText, string Target, string? Note, bool IsRefused)
{
    public bool HasNote => Note is not null;
}

/// <summary>Shows what a file holds before anything is created. Building it reads the file only.</summary>
public sealed class ImportPreviewViewModel
{
    public ImportPreviewViewModel(string fileName, ImportPlan? plan, DocumentError? error)
    {
        FileName = fileName;
        Plan = plan;

        if (error is not null || plan is null)
        {
            Problem = error is null ? Strings.FileProblemNotAnAtlasFile : DocumentText.Describe(error);
            return;
        }

        if (!plan.CanImport)
            Problem = string.Join(Environment.NewLine, plan.WorkspaceErrors.Select(ValidationText.Describe));

        WorkspaceName = plan.Content.Name;
        Description = plan.Content.Description;
        Items = plan.Resources
            .Select(r => new ImportPreviewItem(
                r.Source.Name,
                KindText(r.Kind, r.Source.Kind),
                r.Source.Target,
                DocumentText.Note(r),
                r.Note == ImportNote.Refused))
            .ToList();

        var imported = plan.Resources.Count(r => r.Note != ImportNote.Refused);
        CountText = UiText.Format(Strings.ImportCount, UiText.ResourceCount(imported), plan.Resources.Count);
    }

    public string FileName { get; }

    public ImportPlan? Plan { get; }

    /// <summary>Why the file cannot be imported, or null when it can.</summary>
    public string? Problem { get; }

    public bool HasProblem => Problem is not null;

    public bool CanImport => Plan?.CanImport == true;

    public string WorkspaceName { get; } = string.Empty;

    public string Description { get; } = string.Empty;

    public bool HasDescription => Description.Length > 0;

    public string CountText { get; } = string.Empty;

    public IReadOnlyList<ImportPreviewItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    private static string KindText(ResourceKind? kind, string written)
    {
        if (kind == ResourceKind.Application)
            return Strings.KindApplication;
        if (kind == ResourceKind.File)
            return Strings.KindFile;
        if (kind == ResourceKind.Folder)
            return Strings.KindFolder;

        return kind == ResourceKind.Url ? Strings.KindLink : written;
    }
}
