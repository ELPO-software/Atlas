using Atlas.App.Resources;
using Atlas.App.Services;
using Atlas.App.Text;
using Atlas.Core.Validation;
using Atlas.Core.Workspaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace Atlas.App.ViewModels;

public sealed partial class WorkspaceDetailViewModel(
    Guid workspaceId,
    PageServices services,
    INavigator navigator) : PageViewModel
{
    /// <summary>Notes are saved after this pause in typing.</summary>
    internal static readonly TimeSpan NotesSaveDelay = TimeSpan.FromSeconds(1);

    private readonly ILogger _logger = services.Loggers.CreateLogger<WorkspaceDetailViewModel>();
    private readonly SemaphoreSlim _notesSaveGate = new(1, 1);

    private CancellationTokenSource? _notesDelay;
    private string _savedNotes = string.Empty;
    private bool _isLoadingNotes;
    private bool _isDeleted;
    private int _resourceCount;

    [ObservableProperty]
    public partial bool IsLoaded { get; private set; }

    [ObservableProperty]
    public partial string? LoadError { get; private set; }

    [ObservableProperty]
    public partial string Name { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string Description { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string CreatedText { get; private set; } = string.Empty;

    [ObservableProperty]
    public partial string ModifiedText { get; private set; } = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasNoResources))]
    public partial IReadOnlyList<ResourceGroupViewModel> ResourceGroups { get; private set; } = [];

    [ObservableProperty]
    public partial string Notes { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string? NotesStatus { get; private set; }

    [ObservableProperty]
    public partial string? NotesError { get; private set; }

    [ObservableProperty]
    public partial string? ActionError { get; private set; }

    public bool HasNoResources => ResourceGroups.Count == 0;

    public override bool HasUnsavedChanges => !_isDeleted && !string.Equals(_savedNotes, Notes, StringComparison.Ordinal);

    /// <summary>The automatic save in progress, if any. Lets tests wait for it.</summary>
    internal Task PendingNotesSave { get; private set; } = Task.CompletedTask;

    public override async Task LoadAsync()
    {
        LoadError = null;
        try
        {
            var workspace = await services.Workspaces.GetAsync(workspaceId);

            ShowDetails(workspace);
            ResourceGroups = ResourceGroupViewModel.From(workspace.Resources);
            _resourceCount = workspace.Resources.Count;

            _savedNotes = workspace.Notes;
            _isLoadingNotes = true;
            Notes = workspace.Notes;
            _isLoadingNotes = false;

            IsLoaded = true;
        }
        catch (WorkspaceNotFoundException)
        {
            LoadError = Strings.WorkspaceNotFound;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspace {WorkspaceId} could not be read", workspaceId);
            LoadError = UiText.Format(Strings.WorkspaceReadFailed, services.Paths.LogsDirectory);
        }
    }

    public override async Task<bool> TryLeaveAsync()
    {
        if (!IsLoaded || _isDeleted)
            return true;

        CancelNotesDelay();
        if (await SaveNotesAsync())
            return true;

        return await services.Dialogs.ConfirmAsync(new ConfirmationRequest(
            Strings.LeaveWithoutSavingTitle,
            Strings.LeaveWithoutSavingMessage,
            Strings.LeaveWithoutSaving,
            Strings.Stay));
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task BackAsync() => navigator.OpenWorkspacesAsync();

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task EditAsync()
    {
        ActionError = null;
        var updated = await services.Dialogs.EditWorkspaceAsync(new WorkspaceEditorRequest(
            Strings.EditWorkspaceDialogTitle,
            Strings.Save,
            Name,
            Description,
            ShowDescription: true,
            (name, description) => services.Workspaces.UpdateDetailsAsync(workspaceId, name, description)));

        if (updated is not null)
            ShowDetails(updated);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DuplicateAsync()
    {
        ActionError = null;
        var copy = await services.Dialogs.EditWorkspaceAsync(new WorkspaceEditorRequest(
            Strings.DuplicateWorkspaceDialogTitle,
            Strings.Duplicate,
            SuggestCopyName(Name),
            Description,
            ShowDescription: false,
            (name, _) => DuplicateWithLatestNotesAsync(name)));

        if (copy is not null)
            await navigator.OpenWorkspaceAsync(copy.Id);
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task DeleteAsync()
    {
        ActionError = null;
        var confirmed = await services.Dialogs.ConfirmAsync(new ConfirmationRequest(
            Strings.DeleteWorkspaceTitle,
            UiText.Format(Strings.DeleteWorkspaceMessage, Name, UiText.ResourceCount(_resourceCount)),
            Strings.Delete,
            Strings.Cancel));

        if (!confirmed)
            return;

        CancelNotesDelay();
        await PendingNotesSave;
        try
        {
            await services.Workspaces.DeleteAsync(workspaceId);
            _isDeleted = true;
        }
        catch (WorkspaceNotFoundException)
        {
            _isDeleted = true;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The workspace {WorkspaceId} could not be deleted", workspaceId);
            ActionError = UiText.Format(Strings.DeleteFailed, services.Paths.LogsDirectory);

            // Notes typed before this attempt still have to reach the database.
            if (HasUnsavedChanges)
                ScheduleNotesSave();

            return;
        }

        await navigator.OpenWorkspacesAsync();
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private Task RetrySaveNotesAsync() => SaveNotesAsync();

    partial void OnNotesChanged(string value)
    {
        if (!_isLoadingNotes)
            ScheduleNotesSave();
    }

    private void ScheduleNotesSave()
    {
        NotesStatus = Strings.NotesSaving;
        NotesError = null;

        CancelNotesDelay();
        _notesDelay = new CancellationTokenSource();
        PendingNotesSave = SaveNotesAfterDelayAsync(_notesDelay.Token);
    }

    private void CancelNotesDelay()
    {
        _notesDelay?.Cancel();
        _notesDelay?.Dispose();
        _notesDelay = null;
    }

    private async Task SaveNotesAfterDelayAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(NotesSaveDelay, services.Time, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await SaveNotesAsync();
    }

    /// <summary>
    /// Stores the notes as they are now. Saves run one at a time, and text typed during a save is stored
    /// right after it, so what ends up in the database is always the latest text.
    /// </summary>
    /// <returns>True if nothing remains unsaved.</returns>
    private async Task<bool> SaveNotesAsync()
    {
        await _notesSaveGate.WaitAsync();
        try
        {
            while (!string.Equals(_savedNotes, Notes, StringComparison.Ordinal))
            {
                var text = Notes;
                NotesStatus = Strings.NotesSaving;
                NotesError = null;

                var saved = await services.Workspaces.UpdateNotesAsync(workspaceId, text);

                _savedNotes = text;
                ModifiedText = DateText.Modified(saved.UpdatedAt, services.Time);
            }

            // Also reached when the text went back to what was already stored: there is nothing left to save.
            if (NotesStatus is not null)
                NotesStatus = Strings.NotesSaved;

            return true;
        }
        catch (AtlasValidationException ex)
        {
            NotesStatus = null;
            NotesError = ValidationText.Describe(ex.Errors[0]);
            return false;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "The notes of workspace {WorkspaceId} could not be saved", workspaceId);
            NotesStatus = null;
            NotesError = UiText.Format(Strings.NotesSaveFailed, services.Paths.LogsDirectory);
            return false;
        }
        finally
        {
            _notesSaveGate.Release();
        }
    }

    /// <summary>A copy made right after typing must contain what was just typed.</summary>
    private async Task<Workspace> DuplicateWithLatestNotesAsync(string? name)
    {
        CancelNotesDelay();
        await SaveNotesAsync();
        return await services.Workspaces.DuplicateAsync(workspaceId, name);
    }

    private void ShowDetails(Workspace workspace)
    {
        Name = workspace.Name;
        Description = workspace.Description;
        CreatedText = DateText.Created(workspace.CreatedAt, services.Time);
        ModifiedText = DateText.Modified(workspace.UpdatedAt, services.Time);
    }

    private static string SuggestCopyName(string name)
    {
        var suggestion = UiText.Format(Strings.CopyNameFormat, name);
        var excess = suggestion.Length - WorkspaceRules.NameMaxLength;

        return excess <= 0
            ? suggestion
            : UiText.Format(Strings.CopyNameFormat, name[..^excess].TrimEnd());
    }
}
